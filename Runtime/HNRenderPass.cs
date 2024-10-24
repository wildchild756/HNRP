using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class HNRenderPass
{
    public string passName;

    public List<Slot> inputSlots;
    public List<Slot> outputSlots;

    public void Initialize()
    {
        inputSlots = new List<Slot>();
        outputSlots = new List<Slot>();
    }

}



