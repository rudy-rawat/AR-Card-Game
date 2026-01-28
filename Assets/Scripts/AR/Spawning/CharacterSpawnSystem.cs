using UnityEngine;
using System.Collections.Generic;

public class CharacterSpawnSystem : MonoBehaviour
{
    public static CharacterSpawnSystem Instance;

    public List<SpawnSlot> slots = new();

    void Awake()
    {
        Instance = this;
    }

    public void SpawnCharacter(CardData card, string slotID)
    {
        var slot = slots.Find(s => s.SlotID == slotID);
        if (slot == null || slot.OccupiedBy != null) return;

        GameObject character = Instantiate(card.CharacterPrefab, slot.transform);
        character.transform.localPosition = Vector3.zero;
        character.transform.localRotation = Quaternion.identity;

        slot.OccupiedBy = character.transform;

        Debug.Log($"Spawned {card.CharacterID} in slot {slotID}");
    }
}
