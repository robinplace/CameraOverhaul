global using UnityEngine;
global using HarmonyLib;
using Bindito.Core;
using Timberborn.InputSystem;
using Timberborn.CameraSystem;
using Timberborn.WaterSystemRendering;
using Timberborn.WaterSystemUI;
using Timberborn.SingletonSystem;
using Timberborn.Coordinates;
using Timberborn.SelectionSystem;
using Timberborn.TerrainQueryingSystem;
using Timberborn.GridTraversing;
using Timberborn.CursorToolSystem;
using Timberborn.WaterSystem;
using Timberborn.LevelVisibilitySystem;
using UnityEngine.InputSystem;
using Timberborn.BlueprintSystem;
using Timberborn.OptionsGame;
using Timberborn.ApplicationLifetime;
using Timberborn.UILayoutSystem;
using Timberborn.MainMenuScene;
using Timberborn.Rendering;
using Timberborn.MapStateSystem;
using Timberborn.ModManagerScene;
using Timberborn.SkySystem;
using Timberborn.TimeSystem;
using System;

public class CameraOverhaul : IModStarter
{
	public void StartMod()
	{
		Debug.Log(this.GetType().Name);
		var harmony = new Harmony("Robin.CameraOverhaul");
		harmony.PatchAll();
	}
}

[Context("Game")]
[Context("MapEditor")]
class GameConfigurator: IConfigurator {
	public void Configure(IContainerDefinition c) {
		Debug.Log(this.GetType().Name);
		c.Bind<Cam>().AsSingleton();
		c.Bind<Nav>().AsSingleton();
		c.Bind<Sky>().AsSingleton();
	}
}

enum NavMode {
	Pan,
	Orbit,
}

class Cam(
	CameraService cameraService,
	ISpecService specService
): ILoadableSingleton {
	CameraServiceSpec cameraServiceSpec = null!;
	//GameObject crosshair = GameObject.CreatePrimitive(PrimitiveType.Sphere);
	public void Load() {
		Debug.Log("Cam.Load");
		//crosshair.layer = Layers.IgnoreRaycastMask;
		cameraServiceSpec = specService.GetSingleSpec<CameraServiceSpec>();
		cameraService._camera.farClipPlane = 2 * 1000f;
		cameraService.FreeMode = true;
		RenderSettings.fog = false;
	}
	public Camera camera => cameraService._camera;
	public float distance {
		get => (
			Mathf.Pow(cameraServiceSpec!.ZoomBase, cameraService.ZoomLevel) *
			cameraServiceSpec!.BaseDistance
		);
	}
	public Quaternion rotation {
		get => Quaternion.Euler(
			cameraService.VerticalAngle,
			cameraService.HorizontalAngle,
			0
		);
		set {
			cameraService.VerticalAngle = Vector3.Angle(value * Vector3.up, Vector3.up);
			cameraService.HorizontalAngle = value.eulerAngles.y;
		}
	}
	public Vector3 position {
		get => cameraService.Target + rotation * Vector3.back * distance;
		set {
			var highestOffset = 0f;
			var ray = new Ray(value, rotation * Vector3.forward);
			//Debug.Log("ray " + ray);
			for (var i = 0; i < MapSize.MaxGameTerrainHeight + MapSize.MaxHeightAboveTerrain; i++) {
				var plane = new Plane(Vector3.up, 0 - i);
				if (plane.Raycast(ray, out var offset)) {
					highestOffset = offset;
				}
			}

			var point = ray.GetPoint(highestOffset);
			//crosshair.transform.position = point;
			cameraService.Target = point;
			//Debug.Log("point " + point);

			var zoomLevel = Mathf.Log(
				highestOffset / cameraServiceSpec!.BaseDistance,
				cameraServiceSpec!.ZoomBase
			);
			cameraService.ZoomLevel = zoomLevel;
			//Debug.Log("zoomLevel " + zoomLevel);
		}
	}
}

class Sky(
	Cam cam,
	Sun sunService,
	MapSize mapSize,
	DayStageCycle dayStageCycle,
	InputService inputService
): ILoadableSingleton, ILateUpdatableSingleton {
	GameObject sun = GameObject.CreatePrimitive(PrimitiveType.Sphere);
	GameObject moon = GameObject.CreatePrimitive(PrimitiveType.Sphere);
	public void Load() {
		Debug.Log("Sky.Load");

		sun.transform.localScale = new Vector3(30, 30, 30);
		sun.layer = Layers.IgnoreRaycastMask;
		var sunMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
		//sunMaterial.color = new Color(200, 180, 90);
		sunMaterial.color = new Color(227 / 255f, 221 / 255f, 133 / 255f);
		sun.GetComponent<Renderer>().material = sunMaterial;

		moon.transform.localScale = new Vector3(30, 30, 30);
		moon.layer = Layers.IgnoreRaycastMask;
		var moonMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
		moonMaterial.color = new Color(202 / 255f, 197 / 255f, 182 / 255f);

		moon.GetComponent<Renderer>().material = moonMaterial;
	}

	public void LateUpdateSingleton() {
		Render();
	}

	DayNightCycle dayNightCycle = (DayNightCycle) dayStageCycle._dayNightCycle;
	int inclination = 30;

	void Render() {
		var cameraCenter = new Vector3(cam.position.x, 0, cam.position.z);
		var mapCenter = new Vector3(mapSize.TerrainSize.x * 0.5f, 0, mapSize.TerrainSize.z * 0.5f);
		//var center = new Vector3(0, 0, 0);

		var dayProgress = dayNightCycle.FluidSecondsPassedToday / dayNightCycle.ConfiguredDayLengthInSeconds;

		var sunAngle = 0 - (dayProgress - 1f / 12f) * 360f;
		var moonAngle = 0 - (dayProgress - 7f / 12f) * 360f;

		var sunVector = Quaternion.Euler(0, 0, inclination) * Quaternion.Euler(sunAngle, 0, 0) * Vector3.forward;
		var moonVector = Quaternion.Euler(0, 0, inclination) * Quaternion.Euler(moonAngle, 0, 0) * Vector3.forward;

		sun.transform.position = cameraCenter + sunVector * 400f;
		moon.transform.position = cameraCenter + moonVector * 400f;;

		var lightIntensity = (
			Math.Max(sunVector.y, 0) * 3.0f +
			Math.Max(moonVector.y, 0) * 1.0f
		);
		sunService._sun.intensity = lightIntensity;
		sunService._sun.transform.rotation = Quaternion.LookRotation(Vector3.zero - (
			sunVector.y > 0 ?
			sunVector * 1200f : (
				moonVector.y > 0 ?
				moonVector * 1200f :
				Vector3.down * 1200f
			)
		));

		//var percentX = inputService.MousePosition.x / Display.main.renderingWidth;
	}
}

class Nav(
	Cam cam,
	InputService inputService,
	SelectableObjectRaycaster selectableObjectRaycaster,
	CameraService cameraService,
	TerrainPicker terrainPicker,
	WaterOpacityService waterOpacityService,
	IThreadSafeWaterMap threadSafeWaterMap,
	ILevelVisibilityService levelVisibilityService,
	ISpecService specService
): ILoadableSingleton, IInputProcessor {
	//GameObject crosshair = GameObject.CreatePrimitive(PrimitiveType.Sphere);
	CameraServiceSpec? cameraServiceSpec;

	public void Load() {
		Debug.Log("Nav.Load");
		//crosshair.layer = Layers.IgnoreRaycastMask;
		inputService.AddInputProcessor(this);
		cameraServiceSpec = specService.GetSingleSpec<CameraServiceSpec>();
	}
	
	void TerrainHit(Ray worldRay, out Vector3? worldHit, out float worldDistance) {
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
			var coord = new CursorCoordinates(valueOrDefault.Intersection, vector3Int);
			worldHit = CoordinateSystem.GridToWorld(coord.Coordinates);
			worldDistance = Vector3.Distance(worldRay.origin, worldHit.Value);
			return;
		}
		worldHit = null;
		worldDistance = float.PositiveInfinity;
	}

	void SelectableHit(Ray worldRay, out Vector3? worldHit, out float worldDistance) {
		var didHitSelectable = selectableObjectRaycaster.TryHitSelectableObject(
			worldSpaceRay: worldRay,
			includeTerrainStump: false,
			hitObject: out var _,
			raycastHit: out var selectableHit
		);
		if (didHitSelectable) {
			worldHit = selectableHit.point;
			worldDistance = selectableHit.distance;
			return;
		}
		worldHit = null;
		worldDistance = float.PositiveInfinity;
	}

	void Hit(Ray worldRay, out Vector3? worldHit) {
		TerrainHit(worldRay, out var terrainHit, out var terrainDistance);
		SelectableHit(worldRay, out var selectableHit, out var selectableDistance);
		if (selectableDistance < terrainDistance) {
			worldHit = selectableHit;
		} else if (terrainDistance > 0) {
			worldHit = terrainHit;
		} else {
			worldHit = worldRay.origin;
		}
	}
	
	NavMode? navMode;
	Vector3? orbitOriginWorldPoint;
	Vector2? orbitOriginalScreenPoint;
	Vector3? orbitOriginalCameraPosition;
	Quaternion? orbitOriginalCameraRotation;
	Plane? panWorldPlane;
	Vector3? panOriginalCameraPosition;
	Vector2? panOriginalScreenPoint;

	public bool ProcessInput() {
		Vector2 screenPoint = inputService.MousePosition;
		var worldRay = cameraService.ScreenPointToRayInWorldSpace(screenPoint);
		Hit(worldRay, out var worldHit);
		var zeroPlane = new Plane(Vector3.up, Vector3.zero);
		zeroPlane.Raycast(worldRay, out var zeroOffset);
		var zeroPoint = worldRay.GetPoint(zeroOffset);
		var worldPoint = worldHit ?? zeroPoint;
		//crosshair.transform.position = worldPoint + (cam.position - worldPoint) / 2;
		//crosshair.transform.position = worldPoint;
		//Debug.Log("worldPoint " + worldPoint);

		if (
			inputService.RotateButtonHeld ||
			inputService.MoveButtonHeld && (
				Keyboard.current.leftCommandKey.isPressed ||
				Keyboard.current.rightCommandKey.isPressed ||
				Keyboard.current.leftCtrlKey.isPressed ||
				Keyboard.current.rightCtrlKey.isPressed
			)
		) {
			if (navMode != NavMode.Orbit) {
				// start orbit
				navMode = NavMode.Orbit;
				orbitOriginWorldPoint = worldPoint;
				orbitOriginalScreenPoint = screenPoint;
				orbitOriginalCameraPosition = cam.position;
				orbitOriginalCameraRotation = cam.rotation;
			} else {
				// continue orbit
				var screenDistance = screenPoint - orbitOriginalScreenPoint!.Value;
				var originalVertical = Vector3.Angle(orbitOriginalCameraRotation!.Value * Vector3.up, Vector3.up);
				//Debug.Log("original " + originalVertical);
				var freeAngleDelta = (
					Quaternion.Euler(0, screenDistance.x * 0.1f, 0) *
					orbitOriginalCameraRotation!.Value *
					Quaternion.Euler(Mathf.Clamp(
						0 - screenDistance.y * 0.1f,
						cameraServiceSpec!.VerticalAngleLimits.Min * 0.125f - originalVertical,
						90 - originalVertical
					), 0, 0) *
					Quaternion.Inverse(orbitOriginalCameraRotation!.Value)
				);
				var freeCameraAngle = freeAngleDelta * orbitOriginalCameraRotation!.Value;
				cam.rotation = freeCameraAngle;
				var clampedAngleDelta = cam.rotation * Quaternion.Inverse(orbitOriginalCameraRotation!.Value);

				cam.position = (
					clampedAngleDelta * (orbitOriginalCameraPosition!.Value - orbitOriginWorldPoint!.Value) +
					orbitOriginWorldPoint!.Value
				);
			}
		} else if (inputService.MoveButtonHeld) {
			if (navMode != NavMode.Pan) {
				// start pan
				navMode = NavMode.Pan;
				panWorldPlane = new Plane(Vector3.up, worldPoint);
				panOriginalCameraPosition = cam.position;
				panOriginalScreenPoint = screenPoint;
			} else {
				// continue pan
				var originalWorldRay = cameraService.ScreenPointToRayInWorldSpace(panOriginalScreenPoint!.Value);
				panWorldPlane!.Value.Raycast(worldRay, out var offset);
				var planePoint = worldRay.GetPoint(offset);
				panWorldPlane!.Value.Raycast(originalWorldRay, out var originalOffset);
				var originalPlanePoint = originalWorldRay.GetPoint(originalOffset);
				var worldDistance = originalPlanePoint - planePoint;
				cam.position = panOriginalCameraPosition!.Value + worldDistance;
			}
		} else {
			navMode = null;
		}
		if (!inputService.MouseOverUI && inputService.MouseZoom != 0) {
			var zoomFactor = 1 - inputService.MouseZoom * 1f;
			var minCameraDistance = 0f;
			var maxCameraDistance = (
				Mathf.Pow(cameraServiceSpec!.ZoomBase, cameraServiceSpec.MapEditorZoomLimits.Max * 3f) *
				cameraServiceSpec!.BaseDistance
			);
			var clampedZoomFactor = Mathf.Clamp(
				zoomFactor,
				minCameraDistance / cam.distance,
				maxCameraDistance / cam.distance
			);
			var zoomPoint = worldPoint + (cam.position - worldPoint) * clampedZoomFactor;
			cam.position = zoomPoint;
		}

		//Debug.Log(Mouse.current.scroll.ReadValue());
		//Debug.Log(Keyboard.current.ctrlKey.isPressed);
		//Debug.Log(Keyboard.current.leftCommandKey.isPressed);
		//Debug.Log(Keyboard.current.rightCommandKey.isPressed);

		return false;
	}
}

[HarmonyPatch]
class Patch {
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

	// turn off default zoom handler
	[HarmonyPrefix, HarmonyPatch(typeof(MouseCameraController), nameof(MouseCameraController.ScrollWheelUpdate))]
	static bool ScrollWheelUpdate() {
		return false;
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

	// increase max shadow distance
	[HarmonyPrefix, HarmonyPatch(typeof(ShadowDistanceUpdater), nameof(ShadowDistanceUpdater.LateUpdateSingleton))]
	static bool LateUpdateSingleton(ShadowDistanceUpdater __instance) {
		float distance = Mathf.Clamp(
			Mathf.Max(Mathf.Max(
				__instance.DistanceAtNormalizedScreenPoint(new Vector2(0f, 0f)),
				__instance.DistanceAtNormalizedScreenPoint(new Vector2(0f, 1f))
			), Mathf.Max(
				__instance.DistanceAtNormalizedScreenPoint(new Vector2(1f, 0f)),
				__instance.DistanceAtNormalizedScreenPoint(new Vector2(1f, 1f))
			)),
			0f,
			150 * 5
		);
		if (Mathf.Abs(distance - __instance.GetShadowDistance()) > 0.1f) {
			__instance.SetShadowDistance(distance);
		}
		return false;
	}

	// turn off default sun motion
	[HarmonyPrefix, HarmonyPatch(typeof(Sun), nameof(Sun.UpdateRotation))]
	static bool UpdateRotation() {
		return false;
	}

	/*// force free mode for zoom
	[HarmonyPrefix, HarmonyPatch(typeof(CameraService), nameof(CameraService.ZoomLimitsSpec), MethodType.Getter)]
	static bool ZoomLimitsSpec(CameraService __instance, ref FloatLimitsSpec __result) {
		__result = __instance._cameraServiceSpec.MapEditorZoomLimits;
		return false;
	}*/

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

	/*[HarmonyPrefix, HarmonyPatch(typeof(MainMenuInitializer), nameof(MainMenuInitializer.ShowWelcomeScreen))]
	static bool OverrideWelcomeScreen(MainMenuInitializer __instance) {
		__instance.ShowMainMenuPanel();
		return false;
	}*/
}
