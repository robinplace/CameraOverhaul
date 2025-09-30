global using UnityEngine;
global using HarmonyLib;
using Timberborn.ModManagerScene;
using Timberborn.CameraSystem;

public class CameraOverhaul : IModStarter
{
	public void StartMod()
	{
		Debug.Log(this.GetType().Name);
		var harmony = new Harmony("Robin.CameraOverhaul");
		harmony.PatchAll();
	}
}
