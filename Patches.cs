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
using Timberborn.PrefabOptimization;
using Unity.Mathematics;

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
	public void Load() {
		Debug.Log("Cam.Load");
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
			cameraService.VerticalAngle = Vector3.Angle(value * Vector3.forward, Vector3.up) - 90;
			cameraService.HorizontalAngle = value.eulerAngles.y;
		}
	}
	public Vector3 position {
		get => cameraService.Target + rotation * Vector3.down * distance;
		set {
			cameraService.Target = value - rotation * Vector3.down * distance;
		}
	}
}

class Sky(
	Cam cam,
	CameraService cameraService
): ILoadableSingleton {
	GameObject sun = null!;
	public void Load() {
		Debug.Log("Sky.Load");
		sun = GameObject.CreatePrimitive(PrimitiveType.Sphere);
		sun.transform.localScale = new Vector3(50, 50, 50);
		sun.layer = Layers.IgnoreRaycastMask;
		var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
		material.color = Color.white;
		sun.GetComponent<Renderer>().material = material;
		cameraService.CameraPositionOrRotationChanged += delegate {
			var center = new Vector3(
				cam.position.x,
				0,
				cam.position.z
			);
			var sunOffset = Quaternion.Euler(new Vector3(0, 0, 0)) * Vector3.forward * 1000f;

			sun.transform.position = center + sunOffset;
		};
		//ground.color
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
	//GameObject crosshair = null!;
	CameraServiceSpec? cameraServiceSpec;

	public void Load() {
		Debug.Log("Nav.Load");
		//crosshair = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
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
	Vector3? panOriginalCameraTarget;
	Vector2? panOriginalScreenPoint;

	public bool ProcessInput() {
		Vector2 screenPoint = inputService.MousePosition;
		var worldRay = cameraService.ScreenPointToRayInWorldSpace(screenPoint);
		Hit(worldRay, out var worldHit);
		var zeroPlane = new Plane(Vector3.up, Vector3.zero);
		zeroPlane.Raycast(worldRay, out var zeroOffset);
		var zeroPoint = worldRay.GetPoint(zeroOffset);
		var worldPoint = worldHit ?? zeroPoint;
		//crosshair.transform.position = worldPoint;
		//Debug.Log("worldPoint " + worldPoint);

		var zoomFactor = 1 - inputService.MouseZoom * 1f;

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
				var freeAngleDelta = Quaternion.Euler(
					0 - screenDistance.y * 0.1f,
					screenDistance.x * 0.1f,
					0
				);
				var freeCameraAngle = orbitOriginalCameraRotation!.Value * freeAngleDelta;
				/*var clampedCameraAngle = Quaternion.Euler(
					Mathf.Clamp(
						freeCameraAngle.eulerAngles.x,
						cameraServiceSpec!.VerticalAngleLimits.Min * 0.25f, cameraServiceSpec.VerticalAngleLimits.Max
					),
					freeCameraAngle.eulerAngles.y,
					freeCameraAngle.eulerAngles.z
				);
				var clampedAngleDelta = clampedCameraAngle * Quaternion.Inverse(orbitOriginalCameraAngle!.Value);*/

				cam.rotation = /*clampedCameraAngle*/freeCameraAngle;

				/*cameraService.Target = (
					(
						Quaternion.AngleAxis(clampedAngleDelta.x, Vector3.up) *
						Quaternion.AngleAxis(clampedAngleDelta.y, (
							Quaternion.AngleAxis(orbitOriginalCameraAngle!.Value.x, Vector3.up) *
							Vector3.right
						))
					) *
					(orbitOriginalCameraTarget!.Value - orbitOriginWorldPoint!.Value) +
					orbitOriginWorldPoint!.Value
				);*/

				/*cam.position = (
					(
						freeAngleDelta
					) * (orbitOriginalCameraPosition!.Value - orbitOriginWorldPoint!.Value) +
					orbitOriginWorldPoint!.Value
				)*/;
			}
		} else if (inputService.MoveButtonHeld) {
			if (navMode != NavMode.Pan) {
				// start pan
				navMode = NavMode.Pan;
				panWorldPlane = new Plane(Vector3.up, worldPoint);
				panOriginalCameraTarget = cameraService.Target;
				panOriginalScreenPoint = screenPoint;
			} else {
				// continue pan
				var originalWorldRay = cameraService.ScreenPointToRayInWorldSpace(panOriginalScreenPoint!.Value);
				panWorldPlane!.Value.Raycast(worldRay, out var offset);
				var planePoint = worldRay.GetPoint(offset);
				panWorldPlane!.Value.Raycast(originalWorldRay, out var originalOffset);
				var originalPlanePoint = originalWorldRay.GetPoint(originalOffset);
				var worldDistance = originalPlanePoint - planePoint;
				cameraService.Target = panOriginalCameraTarget!.Value + worldDistance;
			}
		} else {
			navMode = null;
		}
		if (!inputService.MouseOverUI && inputService.MouseZoom != 0) {
			var minCameraDistance = 0f/*(
				Mathf.Pow(cameraServiceSpec!.ZoomBase, float.Epsilon) *
				cameraServiceSpec!.BaseDistance
			)*/;
			var maxCameraDistance = (
				Mathf.Pow(cameraServiceSpec!.ZoomBase, cameraServiceSpec.MapEditorZoomLimits.Max * 3f) *
				cameraServiceSpec!.BaseDistance
			);
			var clampedZoomFactor = Mathf.Clamp(
				zoomFactor,
				minCameraDistance / cam.distance,
				maxCameraDistance / cam.distance
			);

			//var zoomRay = new Ray(cameraPosition, worldPoint.Value - cameraPosition);
			//var zoomPoint = zoomRay.GetPoint(inputService.MouseZoom * 20f);
			var zoomPoint = worldPoint + (cam.position - worldPoint) * clampedZoomFactor;
			//Debug.Log("distance " + Vector3.Distance(cameraPosition, worldPoint.Value) + " by " + (1 - inputService.MouseZoom * 0.1f));

			var targetPlaneY = new Plane(Vector3.up, 0);
			var targetPlaneZ = new Plane(Vector3.forward, 0);
			var targetPlaneX = new Plane(Vector3.right, 0);
			var targetRay = new Ray(zoomPoint, cam.rotation * Vector3.up);
			targetPlaneY.Raycast(targetRay, out var targetPlaneOffsetY);
			targetPlaneZ.Raycast(targetRay, out var targetPlaneOffsetZ);
			targetPlaneX.Raycast(targetRay, out var targetPlaneOffsetX);
			var targetPlaneOffset = Mathf.Min(targetPlaneOffsetY, targetPlaneOffsetZ, targetPlaneOffsetX);
			var targetPoint = targetRay.GetPoint(targetPlaneOffset);
			cameraService.Target = targetPoint;

			var zoomLevel = Mathf.Log(
				targetPlaneOffset / cameraServiceSpec!.BaseDistance,
				cameraServiceSpec!.ZoomBase
			);
			cameraService.ZoomLevel = zoomLevel;

			//Debug.Log(cameraService.ZoomLevel + " to " + zoomLevel);
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
