using UnityEngine;

[CreateAssetMenu(fileName = "CardData", menuName = "ARCardArena/CardData")]
public class CardData : ScriptableObject
{
    public string CardID;       //Unique, sent to server for validation
    public string CharacterID;  //Used By server to spawn Prefab
    public GameObject CharacterPrefab;
    public Sprite CardArtWork;
}
