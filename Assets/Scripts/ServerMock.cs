using System.Collections.Generic;
using UnityEngine;

public static class ServerMock
{
    static Queue<string> availableSlots = new Queue<string>(
        new[] { "A1", "A2", "B1", "B2" });

    public static string AssignSlot(string cardID)
    {
        return availableSlots.Dequeue();
    }
}
