using TMPro;
using UnityEngine;

public class CameraCompass : MonoBehaviour
{
    public Transform player;
    public Transform npc;

    public RectTransform compassArrow; // UI Image стрелка
    public TextMeshProUGUI distanceText; // UI Text расстояние

    void Update()
    {
        if (!player || !npc)
        {
            return;
        }
        
        // Получаем направление между игроком и NPC
        Vector3 direction = npc.position - player.position;
        direction.Normalize();

        // Угол между направлением игрока и направлением на NPC
        float angle = Vector3.SignedAngle(player.forward, direction, Vector3.up);
        compassArrow.localEulerAngles = new Vector3(0, 0, -angle);

        // Расстояние между игроком и NPC
        float distance = Vector3.Distance(player.position, npc.position);
        distanceText.text = distance.ToString("F0") + "m";
    }
}