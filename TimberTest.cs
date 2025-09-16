global using UnityEngine;
global using HarmonyLib;
using Timberborn.ModManagerScene;

public class ModStarter : IModStarter
{
	public void StartMod()
	{
		var harmony = new Harmony("Test.Test");
		harmony.PatchAll();
	}
}
