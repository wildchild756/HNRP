using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DrawTransparentPass : HNRenderPass
{
    public DrawTransparentPass()
    {
        Initialize();

        passName = "Draw Transparent";
    }
}
