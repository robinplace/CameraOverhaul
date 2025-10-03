using UnityEngine;
using HarmonyLib;
using Bindito.Core;
using Timberborn.InputSystem;
using Timberborn.CameraSystem;
using Timberborn.WaterSystemRendering;
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
using Timberborn.ModManagerScene;

public class OverhaulCamera: IModStarter {
	public void StartMod() {
		Debug.Log(this.GetType().Name);
		var harmony = new Harmony("Robin.OverhaulCamera");
		harmony.PatchAll();
	}
}

[Context("Game")]
[Context("MapEditor")]
class CameraConfigurator: IConfigurator {
	public void Configure(IContainerDefinition c) {
		Debug.Log(this.GetType().Name);
		c.Bind<Cam>().AsSingleton();
		c.Bind<Nav>().AsSingleton();
	}
}

enum NavMode {
	Pan,
	Orbit,
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
	GameObject crosshair = Utility.crosshair();
	CameraServiceSpec? cameraServiceSpec;

	public void Load() {
		Debug.Log("Nav.Load");
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
		crosshair.transform.localPosition = worldPoint;
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
				var originalVertical = Vector3.Angle(orbitOriginalCameraRotation!.Value * Vector3.forward, Vector3.up) - 90;
				//Debug.Log("original " + originalVertical);
				var freeAngleDelta = (
					Quaternion.Euler(0, screenDistance.x * 0.1f, 0) *
					orbitOriginalCameraRotation!.Value *
					Quaternion.Euler(Mathf.Clamp(
						0 - screenDistance.y * 0.1f,
						0 - 90 - originalVertical + float.Epsilon,
						90 - originalVertical - float.Epsilon
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
			var minCameraDistance = float.Epsilon;
			var maxCameraDistance = (
				Mathf.Pow(cameraServiceSpec!.ZoomBase, cameraServiceSpec.MapEditorZoomLimits.Max * 2f) *
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
class CameraPatch {
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
}
