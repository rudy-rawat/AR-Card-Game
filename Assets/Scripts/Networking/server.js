const WebSocket = require('ws');

class Player {
    constructor(id, ws, cardID) {
        this.id = id;
        this.ws = ws;
        this.cardID = cardID;  // Card ID for this player
        this.assignedSlot = null;  // Parking slot (A1, A2, B1, B2)
        this.health = 100;
        this.isAlive = true;
        this.lastHeartbeat = Date.now();
    }

    takeDamage(damage) {
        this.health = Math.max(0, this.health - damage);
        if (this.health <= 0) {
            this.isAlive = false;
        }
    }

    toJSON() {
        return {
            id: this.id,
            cardID: this.cardID,
            assignedSlot: this.assignedSlot,
            health: this.health,
            isAlive: this.isAlive
        };
    }
}

class GameServer {
    constructor(port = 8080) {
        this.port = port;
        this.wss = null;
        this.players = new Map();
        this.playersByCardID = new Map(); // Track players by card ID
        this.nextPlayerId = 1;
        this.ATTACK_DAMAGE = 10;
        this.MAX_PLAYERS = 2;
        this.HEARTBEAT_INTERVAL = 30000; // 30 seconds
        
        // Available parking slots
        this.availableSlots = ["A1", "A2", "B1", "B2"];
        this.assignedSlots = new Map(); // cardID -> slot
    }

    start() {
        this.wss = new WebSocket.Server({ port: this.port });

        console.log(`\n🎮 Game Server started on port ${this.port}`);
        console.log(`⏳ Waiting for ${this.MAX_PLAYERS} players to connect...\n`);

        this.wss.on('connection', (ws) => {
            this.handleConnection(ws);
        });

        // Start heartbeat checker
        setInterval(() => this.checkHeartbeats(), 5000);
    }

    handleConnection(ws) {
        // Wait for initial message with cardID
        ws.once('message', (data) => {
            try {
                const message = JSON.parse(data.toString());
                
                if (message.type === 'CONNECT' && message.cardID) {
                    this.processConnection(ws, message.cardID);
                } else {
                    this.sendToClient(ws, {
                        type: 'ERROR',
                        message: 'Invalid connection request. CardID required.'
                    });
                    ws.close();
                }
            } catch (error) {
                console.error('Connection error:', error.message);
                ws.close();
            }
        });
    }

    processConnection(ws, cardID) {
        // Check if server is full
        if (this.players.size >= this.MAX_PLAYERS) {
            this.sendToClient(ws, {
                type: 'FULL',
                message: 'Server is full (2/2 players)'
            });
            ws.close();
            console.log(`❌ Connection refused from Card ${cardID} - Server full`);
            return;
        }

        // Check if cardID already connected
        if (this.playersByCardID.has(cardID)) {
            this.sendToClient(ws, {
                type: 'ERROR',
                message: 'Card ID already in use'
            });
            ws.close();
            console.log(`❌ Connection refused - Card ${cardID} already connected`);
            return;
        }

        // Assign a slot to this card
        const assignedSlot = this.assignSlot(cardID);
        
        if (!assignedSlot) {
            this.sendToClient(ws, {
                type: 'ERROR',
                message: 'No available slots'
            });
            ws.close();
            console.log(`❌ Connection refused - No slots available`);
            return;
        }

        // Create new player
        const playerId = this.nextPlayerId++;
        const player = new Player(playerId, ws, cardID);
        player.assignedSlot = assignedSlot;
        
        this.players.set(playerId, player);
        this.playersByCardID.set(cardID, player);

        console.log(`✅ Card ${cardID} connected as Player ${playerId}`);
        console.log(`   Assigned Slot: ${assignedSlot}`);
        console.log(`   Players online: ${this.players.size}/${this.MAX_PLAYERS}\n`);

        // Send connection confirmation
        this.sendToClient(ws, {
            type: 'CONNECTED',
            playerId: playerId,
            cardID: cardID,
            assignedSlot: assignedSlot,
            message: `Connected as Player ${playerId} with Card ${cardID}`
        });

        // Broadcast game state to all players
        this.broadcastGameState();

        // Setup message handler
        ws.on('message', (data) => {
            this.handleMessage(playerId, cardID, data);
        });

        // Handle disconnection
        ws.on('close', () => {
            this.handleDisconnect(playerId, cardID);
        });

        ws.on('error', (error) => {
            console.error(`❌ WebSocket error for Card ${cardID}:`, error.message);
        });
    }

    assignSlot(cardID) {
        if (this.availableSlots.length === 0) {
            return null;
        }

        const slot = this.availableSlots.shift();
        this.assignedSlots.set(cardID, slot);
        console.log(`🅿️  Assigned slot ${slot} to Card ${cardID}`);
        return slot;
    }

    releaseSlot(cardID) {
        if (this.assignedSlots.has(cardID)) {
            const slot = this.assignedSlots.get(cardID);
            this.assignedSlots.delete(cardID);
            this.availableSlots.push(slot);
            console.log(`🅿️  Released slot ${slot} from Card ${cardID}`);
            return slot;
        }
        return null;
    }

    handleMessage(playerId, cardID, data) {
        try {
            const message = JSON.parse(data.toString());
            const player = this.players.get(playerId);

            if (!player) return;

            switch (message.type) {
                case 'ATTACK':
                    // Use cardID from message or player's cardID
                    const attackerCardID = message.cardID || cardID;
                    this.handleAttack(playerId, attackerCardID);
                    break;

                case 'ATTACK_BY_CARD':
                    // Attack using specific cardID
                    if (message.targetCardID) {
                        this.handleAttackByCard(cardID, message.targetCardID);
                    }
                    break;

                case 'HEARTBEAT':
                    player.lastHeartbeat = Date.now();
                    break;

                case 'GET_STATE':
                    this.sendGameState(player.ws);
                    break;

                case 'GET_SLOT':
                    // Return assigned slot for this cardID
                    this.sendToClient(player.ws, {
                        type: 'SLOT_INFO',
                        cardID: cardID,
                        slot: player.assignedSlot
                    });
                    break;

                default:
                    console.log(`⚠️  Unknown message type: ${message.type}`);
            }
        } catch (error) {
            console.error(`❌ Error handling message from Card ${cardID}:`, error.message);
        }
    }

    handleAttack(attackerId, attackerCardID) {
        const attacker = this.players.get(attackerId);

        if (!attacker || !attacker.isAlive) {
            this.sendToClient(attacker.ws, {
                type: 'ERROR',
                message: 'You are dead and cannot attack!'
            });
            return;
        }

        // Find opponent
        let opponent = null;
        for (const [id, player] of this.players) {
            if (id !== attackerId) {
                opponent = player;
                break;
            }
        }

        if (!opponent) {
            this.sendToClient(attacker.ws, {
                type: 'ERROR',
                message: 'No opponent found. Waiting for another player...'
            });
            return;
        }

        // Apply damage
        opponent.takeDamage(this.ATTACK_DAMAGE);

        console.log(`⚔️  Card ${attackerCardID} (Player ${attackerId}) attacked Card ${opponent.cardID} (Player ${opponent.id})!`);
        console.log(`   Player ${opponent.id} health: ${opponent.health}/100`);
        console.log(`   Slot ${attacker.assignedSlot} → Slot ${opponent.assignedSlot}\n`);

        // Send attack notification
        this.sendToClient(attacker.ws, {
            type: 'ATTACK_SUCCESS',
            damage: this.ATTACK_DAMAGE,
            targetId: opponent.id,
            targetCardID: opponent.cardID,
            targetSlot: opponent.assignedSlot,
            targetHealth: opponent.health
        });

        this.sendToClient(opponent.ws, {
            type: 'ATTACKED',
            attackerId: attackerId,
            attackerCardID: attackerCardID,
            attackerSlot: attacker.assignedSlot,
            damage: this.ATTACK_DAMAGE,
            newHealth: opponent.health
        });

        // Check for game over
        if (!opponent.isAlive) {
            console.log(`\n🏆 GAME OVER! Card ${attackerCardID} (Slot ${attacker.assignedSlot}) wins!\n`);

            this.sendToClient(attacker.ws, {
                type: 'VICTORY',
                message: `You defeated Card ${opponent.cardID} in Slot ${opponent.assignedSlot}!`,
                defeatedCardID: opponent.cardID
            });

            this.sendToClient(opponent.ws, {
                type: 'DEFEAT',
                message: `You were defeated by Card ${attackerCardID} in Slot ${attacker.assignedSlot}!`,
                winnerCardID: attackerCardID
            });
        }

        // Broadcast updated game state
        this.broadcastGameState();
    }

    handleAttackByCard(attackerCardID, targetCardID) {
        const attacker = this.playersByCardID.get(attackerCardID);
        const target = this.playersByCardID.get(targetCardID);

        if (!attacker) {
            console.log(`❌ Attacker Card ${attackerCardID} not found`);
            return;
        }

        if (!attacker.isAlive) {
            this.sendToClient(attacker.ws, {
                type: 'ERROR',
                message: 'You are dead and cannot attack!'
            });
            return;
        }

        if (!target) {
            this.sendToClient(attacker.ws, {
                type: 'ERROR',
                message: `Target Card ${targetCardID} not found`
            });
            return;
        }

        if (!target.isAlive) {
            this.sendToClient(attacker.ws, {
                type: 'ERROR',
                message: `Target Card ${targetCardID} is already dead`
            });
            return;
        }

        // Apply damage
        target.takeDamage(this.ATTACK_DAMAGE);

        console.log(`⚔️  Card ${attackerCardID} (Slot ${attacker.assignedSlot}) attacked Card ${targetCardID} (Slot ${target.assignedSlot})!`);
        console.log(`   Card ${targetCardID} health: ${target.health}/100\n`);

        // Send notifications
        this.sendToClient(attacker.ws, {
            type: 'ATTACK_SUCCESS',
            damage: this.ATTACK_DAMAGE,
            targetId: target.id,
            targetCardID: targetCardID,
            targetSlot: target.assignedSlot,
            targetHealth: target.health
        });

        this.sendToClient(target.ws, {
            type: 'ATTACKED',
            attackerId: attacker.id,
            attackerCardID: attackerCardID,
            attackerSlot: attacker.assignedSlot,
            damage: this.ATTACK_DAMAGE,
            newHealth: target.health
        });

        // Check for game over
        if (!target.isAlive) {
            console.log(`\n🏆 Card ${attackerCardID} wins!\n`);

            this.sendToClient(attacker.ws, {
                type: 'VICTORY',
                message: `You defeated Card ${targetCardID}!`,
                defeatedCardID: targetCardID
            });

            this.sendToClient(target.ws, {
                type: 'DEFEAT',
                message: `You were defeated by Card ${attackerCardID}!`,
                winnerCardID: attackerCardID
            });
        }

        // Broadcast game state
        this.broadcastGameState();
    }

    handleDisconnect(playerId, cardID) {
        const player = this.players.get(playerId);
        if (player) {
            this.players.delete(playerId);
            this.playersByCardID.delete(cardID);
            this.releaseSlot(cardID);
            console.log(`👋 Card ${cardID} (Player ${playerId}) disconnected (${this.players.size}/${this.MAX_PLAYERS})`);
            this.broadcastGameState();
        }
    }

    checkHeartbeats() {
        const now = Date.now();
        const playersToRemove = [];

        for (const [id, player] of this.players) {
            if (now - player.lastHeartbeat > this.HEARTBEAT_INTERVAL) {
                playersToRemove.push(id);
            }
        }

        playersToRemove.forEach(id => {
            console.log(`💔 Player ${id} timed out (no heartbeat)`);
            const player = this.players.get(id);
            if (player && player.ws) {
                player.ws.close();
            }
            this.players.delete(id);
        });

        if (playersToRemove.length > 0) {
            this.broadcastGameState();
        }
    }

    broadcastGameState() {
        const gameState = this.getGameState();
        const message = {
            type: 'GAME_STATE',
            players: gameState,
            playerCount: this.players.size
        };

        for (const player of this.players.values()) {
            this.sendToClient(player.ws, message);
        }
    }

    sendGameState(ws) {
        const gameState = this.getGameState();
        this.sendToClient(ws, {
            type: 'GAME_STATE',
            players: gameState,
            playerCount: this.players.size
        });
    }

    getGameState() {
        const state = [];
        for (const player of this.players.values()) {
            state.push(player.toJSON());
        }
        return state;
    }

    sendToClient(ws, data) {
        if (ws && ws.readyState === WebSocket.OPEN) {
            ws.send(JSON.stringify(data));
        }
    }
}

// Start the server
const server = new GameServer(8080);
server.start();

// Handle graceful shutdown
process.on('SIGINT', () => {
    console.log('\n\n👋 Shutting down server...');
    process.exit(0);
});