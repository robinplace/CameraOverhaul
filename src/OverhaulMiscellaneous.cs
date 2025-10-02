using UnityEngine;
using HarmonyLib;
using Timberborn.WaterSystemRendering;
using Timberborn.WaterSystemUI;
using Timberborn.OptionsGame;
using Timberborn.ApplicationLifetime;
using Timberborn.UILayoutSystem;
using Timberborn.MainMenuScene;
using Timberborn.ModManagerScene;

public class MiscellaneousOverhaul : IModStarter {
	public void StartMod() {
		Debug.Log(this.GetType().Name);
		var harmony = new Harmony("Robin.MiscellaneousOverhaul");
		harmony.PatchAll();
	}
}

[HarmonyPatch]
class MiscellaneousPatch {
	// allow water toggle only explicitly i.e. from the panel & its keybind
	[HarmonyPrefix, HarmonyPatch(typeof(WaterOpacityToggle), nameof(WaterOpacityToggle.HideWater))]
	static bool HideWater() {
		return new System.Diagnostics.StackFrame(2).GetMethod().Name == nameof(WaterOpacityTogglePanel.OnWaterToggled);
	}

	// allow water toggle only explicitly i.e. from the panel & its keybind
	[HarmonyPrefix, HarmonyPatch(typeof(WaterOpacityToggle), nameof(WaterOpacityToggle.ShowWater))]
	static bool ShowWater() {
		return new System.Diagnostics.StackFrame(2).GetMethod().Name == nameof(WaterOpacityTogglePanel.OnWaterToggled);
	}

	// turn off goodbye on quit to main menu
	[HarmonyPrefix, HarmonyPatch(typeof(GameOptionsBox), nameof(GameOptionsBox.ExitToMenuClicked))]
	static bool ExitToMenuClicked(GameOptionsBox __instance) {
		__instance._goodbyeBoxFactory._mainMenuSceneLoader.OpenMainMenu();
		return false;
	}

	// turn off goodbye on quit to desktop
	[HarmonyPrefix, HarmonyPatch(typeof(GameOptionsBox), nameof(GameOptionsBox.ExitToDesktopClicked))]
	static bool ExitToDesktopClicked() {
		GameQuitter.Quit();
		return false;
	}

	// turn off panel pause
	[HarmonyPrefix, HarmonyPatch(typeof(OverlayPanelSpeedLocker), nameof(OverlayPanelSpeedLocker.OnPanelShown))]
	static bool OnPanelShown() {
		return false;
	}

	// turn off panel unpause
	[HarmonyPrefix, HarmonyPatch(typeof(OverlayPanelSpeedLocker), nameof(OverlayPanelSpeedLocker.OnPanelHidden))]
	static bool OnPanelHidden() {
		return false;
	}

	// turn off welcome screen
	[HarmonyPrefix, HarmonyPatch(typeof(MainMenuInitializer), nameof(MainMenuInitializer.ShowWelcomeScreen))]
	static bool ShowWelcomeScreen(MainMenuInitializer __instance) {
		__instance.ShowMainMenuPanel();
		//if (!Application.isFocused) {
		__instance._mainMenuPanel.LoadMostRecentSave();
		//}
		return false;
	}
}
