using UnityEngine;
using Vuforia;

public class CardTracker : MonoBehaviour
{
    public CardData cardData;

    ObserverBehaviour observer;

    void Awake()
    {
        observer = GetComponent<ObserverBehaviour>();
        observer.OnTargetStatusChanged += OnStatusChanged;
    }

    void OnStatusChanged(ObserverBehaviour behaviour, TargetStatus status)
    {
        if (status.Status == Status.TRACKED)
        {
            OnCardDetected();
        }
    }

    void OnCardDetected()
    {
        Debug.Log("Card Detected: " + cardData.CardID);
        ARCardManager.Instance.RegisterCard(cardData, transform);
    }
}