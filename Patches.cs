using Bindito.Core;
using Timberborn.InputSystem;
using Timberborn.CameraSystem;
using Timberborn.WaterSystemRendering;
using Newtonsoft.Json.Utilities;
using Timberborn.WaterSystemUI;
using Unity.Services.Core.Telemetry.Internal;
using Timberborn.SingletonSystem;
using Timberborn.AssetSystem;
using Timberborn.Debugging;
using Timberborn.RootProviders;
using Timberborn.Coordinates;
using Timberborn.SelectionSystem;
using Timberborn.BaseComponentSystem;
using Timberborn.BlockSystem;
using Timberborn.TerrainQueryingSystem;
using Timberborn.GridTraversing;
using Timberborn.CursorToolSystem;
using Timberborn.TerrainSystem;
using Timberborn.PrefabOptimization;
using Timberborn.Rendering;
using Timberborn.BlockObjectPickingSystem;
using Timberborn.BlockSystemNavigation;
using UnityEngine.Rendering.Universal.UTess;
using UnityEngine.Rendering;
using Timberborn.WaterSystem;
using Timberborn.LevelVisibilitySystem;
using Mono.Cecil.Cil;
using UnityEngine.InputSystem;
using Timberborn.MainMenuScene;
using Timberborn.MainMenuPanels;

[Context("Game")]
[Context("MapEditor")]
internal class TestConfigurator : IConfigurator {
	public void Configure(IContainerDefinition c) {
		Debug.Log("Test Conf");
		c.Bind<Indicator>().AsSingleton();
	}
}

public class AutoContinue(
    MainMenuPanel mainMenuPanel
) : IUpdatableSingleton {
	public void UpdateSingleton() {
		if (!Keyboard.current.shiftKey.isPressed) {
			mainMenuPanel.ContinueClicked(null);
		}
	}
}

enum NavMode {
	Pan,
	Orbit,
}

class Indicator(
	InputService inputService,
	SelectableObjectRaycaster selectableObjectRaycaster,
	CameraService cameraService,
	TerrainPicker terrainPicker,
	ITerrainService terrainService,
	WaterOpacityService waterOpacityService,
	IThreadSafeWaterMap threadSafeWaterMap,
	ILevelVisibilityService levelVisibilityService
) : ILoadableSingleton, IInputProcessor {
	GameObject _crosshair = null!;

	public void Load() {
		Debug.Log("Indic Load");
		_crosshair = GameObject.CreatePrimitive(PrimitiveType.Sphere);
		_crosshair.layer = Layers.IgnoreRaycastMask;
		inputService.AddInputProcessor(this);
	}
	
	void TerrainHit(Ray worldRay, out Vector3? worldPoint, out float worldDistance) {
		var gridRay = CoordinateSystem.WorldToGrid(worldRay);
		var terrainCoord = (
			waterOpacityService.IsWaterTransparent ?
			terrainPicker.PickTerrainCoordinates(gridRay, terrainPicker.IsTerrainVoxel) :
			terrainPicker.PickTerrainCoordinates(gridRay, (Vector3Int coord) => (
				(threadSafeWaterMap.CellIsUnderwater(coord) && coord.z <= levelVisibilityService.MaxVisibleLevel) ||
				terrainPicker.IsTerrainVoxel(coord)
			))
		);
		if (terrainCoord.HasValue) {
			TraversedCoordinates valueOrDefault = terrainCoord.GetValueOrDefault();
			Vector3Int vector3Int = valueOrDefault.Coordinates + valueOrDefault.Face;
			if (terrainService.Contains(vector3Int)) {
				var coord = new CursorCoordinates(valueOrDefault.Intersection, vector3Int);
				worldPoint = CoordinateSystem.GridToWorld(coord.Coordinates);
				worldDistance = Vector3.Distance(worldRay.origin, worldPoint.Value);
				return;
			}
		}
		worldPoint = null;
		worldDistance = float.PositiveInfinity;
	}
	void SelectableHit(Ray worldRay, out Vector3? worldPoint, out float worldDistance) {
		var didHitSelectable = selectableObjectRaycaster.TryHitSelectableObject(
			worldSpaceRay: worldRay,
			includeTerrainStump: false,
			hitObject: out var _,
			raycastHit: out var selectableHit
		);
		if (didHitSelectable) {
			worldPoint = selectableHit.point;
			worldDistance = selectableHit.distance;
			return;
		}
		worldPoint = null;
		worldDistance = float.PositiveInfinity;
	}

	void Hit(Ray worldRay, out Vector3? worldPoint) {
		TerrainHit(worldRay, out var terrainPoint, out var terrainDistance);
		SelectableHit(worldRay, out var selectablePoint, out var selectableDistance);
		if (selectableDistance < terrainDistance) {
			worldPoint = selectablePoint;
		} else if (terrainDistance > 0) {
			worldPoint = terrainPoint;
		} else {
			worldPoint = worldRay.origin;
		}
	}
	
	NavMode? navMode;
	float? panZ;
	Vector3? orbitLastScreenPoint;
	Vector3? panOriginalWorldPoint;
	Vector3? panOriginalCameraTarget;

	public bool ProcessInput() {
		if (inputService.MoveButtonHeld) {
			var screenPoint = inputService.MousePosition;
			var worldRay = cameraService.ScreenPointToRayInWorldSpace(screenPoint);

			if (
				Keyboard.current.leftCommandKey.isPressed ||
				Keyboard.current.rightCommandKey.isPressed ||
				Keyboard.current.leftShiftKey.isPressed ||
				Keyboard.current.rightShiftKey.isPressed
			) {
				if ( navMode != NavMode.Orbit) {
					// start orbit
					Hit(worldRay, out var worldPoint);
					if (worldPoint.HasValue) {
						navMode = NavMode.Orbit;
						orbitLastScreenPoint = screenPoint;
					}
				} else {
					// continue orbit
				}
			} else {
				if (navMode != NavMode.Pan) {
					// start pan
					Hit(worldRay, out var worldPoint);
					if (worldPoint.HasValue) {
						navMode = NavMode.Pan;
						panZ = worldPoint.Value.z;
						panOriginalWorldPoint = worldPoint;
						panOriginalCameraTarget = cameraService.Target;
					}
				} else {
					// continue pan
					var panWorldPlane = new Plane(Vector3.up, panZ!.Value);
					panWorldPlane.Raycast(worldRay, out var enterOffset);
					var panWorldPoint = worldRay.GetPoint(enterOffset);
					_crosshair.transform.position = panOriginalWorldPoint!.Value;
					/*_cameraService.Target = (
						panOriginalCameraTarget!.Value +
						(panWorldPoint - panOriginalWorldPoint!.Value)
					);*/
					Debug.Log("continue pan");
					Debug.Log(enterOffset);
					Debug.Log(panWorldPoint - panOriginalWorldPoint!.Value);
					Debug.Log(panOriginalCameraTarget!.Value);
				}
			}
		} else {
			navMode = null;
		}

		//Debug.Log(Mouse.current.scroll.ReadValue());
		//Debug.Log(Keyboard.current.ctrlKey.isPressed);
		//Debug.Log(Keyboard.current.leftCommandKey.isPressed);
		//Debug.Log(Keyboard.current.rightCommandKey.isPressed);

		return false;
	}
}

[HarmonyPatch]
class Patches {
	/*[HarmonyPrefix, HarmonyPatch(typeof(MouseController), nameof(MouseController.HideCursor))]
	static bool PatchedHideCursor() {
		Debug.Log(new System.Diagnostics.StackTrace());
		return true;
	}*/

	/*[HarmonyPrefix, HarmonyPatch(typeof(CursorDebugger), nameof(CursorDebugger.Show))]
	static void Show(CursorDebugger __instance) {
		Debug.Log("Show");
	}*/

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

	// turn off default pan handler
	[HarmonyPrefix, HarmonyPatch(typeof(MouseCameraController), nameof(MouseCameraController.MovementUpdate))]
	static bool MovementUpdate() {
		return false;
	}

	// turn off default orbit handler
	[HarmonyPrefix, HarmonyPatch(typeof(MouseCameraController), nameof(MouseCameraController.RotationUpdate))]
	static bool RotationUpdate() {
		return false;
	}

	/*[HarmonyPrefix, HarmonyPatch(typeof(MouseCameraController), nameof(MouseCameraController.StartRotatingCamera))]
	static bool PatchedStartRotatingCamera(MouseCameraController __instance) {
		Debug.Log("PatchedStartRotatingCamera");
		Debug.Log(new System.Diagnostics.StackTrace());
		__instance._rotating = true;
		__instance._rotationDistanceAccumulator = 0f;
		return false;
	}

	[HarmonyPrefix, HarmonyPatch(typeof(MouseCameraController), nameof(MouseCameraController.StopRotatingCamera))]
	static bool PatchedStopRotatingCamera(MouseCameraController __instance) {
		Debug.Log("PatchedStopRotatingCamera");
		Debug.Log("Try Spec Read");
		Debug.Log(__instance._mouseCameraControllerSpec.RmbRotationSpeed);
		Debug.Log("Got Spec Read");
		Debug.Log(new System.Diagnostics.StackTrace());
		__instance._rotating = false;
		__instance._rotationDistanceAccumulator = 0f;
		__instance._cameraActionMarker.Hide();
		return false;
	}

	[HarmonyPrefix, HarmonyPatch(typeof(MouseCameraController), nameof(MouseCameraController.RotateCamera))]
	static bool PatchedRotateCamera(MouseCameraController __instance) {
		Debug.Log("PatchedRotateCamera");
		Vector2 mouseXYAxes = __instance._inputService.MouseXYAxes;
		__instance._rotationDistanceAccumulator += mouseXYAxes.magnitude;

		Vector2 vector = 0.67f * mouseXYAxes;
		__instance._cameraService.ModifyHorizontalAngle(vector.x);
		__instance._cameraService.ModifyVerticalAngle(0f - vector.y);

		return false;
	}*/

	[HarmonyPrefix, HarmonyPatch(typeof(MainMenuInitializer), nameof(MainMenuInitializer.ShowWelcomeScreen))]
	static bool OverrideWelcomeScreen(MainMenuInitializer __instance) {
		__instance.ShowMainMenuPanel();
		return false;
	}
}

// switch between pan & orbit with a key
// don't pause when the happiness panel is opened
