using System.Collections.Generic;
using UnityEngine;

public class ARCardManager : MonoBehaviour
{
    public static ARCardManager Instance;

    Dictionary<string, Transform> detectedCards = new();

    void Awake()
    {
        Instance = this;
    }

    public void RegisterCard(CardData card, Transform cardTransform)
    {
        if (detectedCards.ContainsKey(card.CardID)) return;

        detectedCards.Add(card.CardID, cardTransform);

        Debug.Log("Registered Card: " + card.CardID);

        // Later: Send to Server
        // Network.SendCardDetected(card.CardID, cardTransform.pose);
    }
}
