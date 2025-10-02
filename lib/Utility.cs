using UnityEngine;
using Timberborn.CameraSystem;
using Timberborn.SingletonSystem;
using Timberborn.BlueprintSystem;
using Timberborn.Rendering;
using Timberborn.MapStateSystem;

class Utility {
	public delegate void Transformer(Transform transform);
	public static GameObject crosshair(
		PrimitiveType? type = null,
		Color? color = null,
		Transformer? transformer = null
	) {
		var crosshair = GameObject.CreatePrimitive(type ?? PrimitiveType.Sphere);
		crosshair.SetActive(false);
		crosshair.layer = Layers.IgnoreRaycastMask;
		if (transformer != null) {
			transformer(crosshair.transform);
		}
		var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
		material.color = color ?? Color.magenta;
		crosshair.GetComponent<Renderer>().material = material;
		var container = new GameObject();
		crosshair.transform.parent = container.transform;
		return container;
	}
}

class Cam(
	CameraService cameraService,
	ISpecService specService,
	MapSize mapSize
): ILoadableSingleton, ILateUpdatableSingleton {
	CameraServiceSpec cameraServiceSpec = null!;
	GameObject crosshair = Utility.crosshair(PrimitiveType.Sphere, Color.white);
	GameObject ground = null!;

	public void Load() {
		Debug.Log("Cam.Load");
		cameraServiceSpec = specService.GetSingleSpec<CameraServiceSpec>();
		cameraService._camera.farClipPlane = 2 * 1000f;
		RenderSettings.fog = false;
		cameraService.FreeMode = true;

		ground = GameObject.CreatePrimitive(PrimitiveType.Quad);
		ground.layer = Layers.IgnoreRaycastMask;
		var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
		material.color = Color.black;
		ground.GetComponent<Renderer>().material = material;
		ground.transform.localRotation = Quaternion.Euler(0 - 90, 0, 0);
	}

	public void LateUpdateSingleton() {
		ground.transform.localPosition = new Vector3(mapSize.TerrainSize.x / 2, 0 - 1.15f, mapSize.TerrainSize.y / 2);
		ground.transform.localScale = new Vector3(mapSize.TerrainSize.x, mapSize.TerrainSize.y, 1);
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
			Debug.Log("value " + value * Vector3.forward);
			Debug.Log("angle " + (Vector3.Angle(value * Vector3.forward, Vector3.up) - 90));
			cameraService.VerticalAngle = Vector3.Angle(value * Vector3.forward, Vector3.up) - 90;
			cameraService.HorizontalAngle = value.eulerAngles.y;
		}
	}
	public Vector3 position {
		get => cameraService.Target + rotation * Vector3.back * distance;
		set {
			var mapCenter = new Vector3(mapSize.TerrainSize.x * 0.5f, 0, mapSize.TerrainSize.y * 0.5f);
			var ray = new Ray(value, rotation * Vector3.forward);

			var planeArray = new Plane[] {
				new Plane(Vector3.up, 0),
				new Plane(Vector3.left, 0 - CameraService.FreeModeMapMargin),
				new Plane(Vector3.left, mapSize.TerrainSize.x + CameraService.FreeModeMapMargin),
				new Plane(Vector3.back, 0 - CameraService.FreeModeMapMargin),
				new Plane(Vector3.back, mapSize.TerrainSize.y + CameraService.FreeModeMapMargin)
			};

			var minimumDistance = float.PositiveInfinity;
			var mostCentralPoint = mapCenter;
			var mostCentralPointOffset = 20f;
			for (var i = 0; i < planeArray.Length; i++) {
				if (planeArray[i].Raycast(ray, out var offset)) {
					var point = ray.GetPoint(offset);
					var distance = Vector3.Distance(mapCenter, point);
					if (distance < minimumDistance) {
						mostCentralPoint = point;
						mostCentralPointOffset = offset;
						minimumDistance = distance;
					}
				}
			}

			crosshair.transform.localPosition = mostCentralPoint;
			cameraService.Target = mostCentralPoint;
			//Debug.Log("point " + point);

			var zoomLevel = Mathf.Log(
				mostCentralPointOffset / cameraServiceSpec!.BaseDistance,
				cameraServiceSpec!.ZoomBase
			);
			cameraService.ZoomLevel = zoomLevel;
			//Debug.Log("zoomLevel " + zoomLevel);
		}
	}
}
