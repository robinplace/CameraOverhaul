using UnityEngine;
using HarmonyLib;
using Bindito.Core;
using Timberborn.CameraSystem;
using Timberborn.SingletonSystem;
using Timberborn.Rendering;
using Timberborn.MapStateSystem;
using Timberborn.ModManagerScene;
using Timberborn.SkySystem;
using Timberborn.TimeSystem;
using System.Reflection;

public class SkyOverhaul : IModStarter {
	public void StartMod() {
		Debug.Log(this.GetType().Name);
		var harmony = new Harmony("Robin.SkyOverhaul");
		harmony.PatchAll();
	}
}

[Context("Game")]
[Context("MapEditor")]
class SkyConfigurator: IConfigurator {
	public void Configure(IContainerDefinition c) {
		Debug.Log(this.GetType().Name);
		c.Bind<Cam>().AsSingleton();
		c.Bind<Sky>().AsSingleton();
	}
}

class Sky(
	Cam cam,
	Sun sunService,
	MapSize mapSize,
	DayStageCycle dayStageCycle
): ILoadableSingleton, ILateUpdatableSingleton {
	GameObject upCrosshair = Utility.crosshair(
		PrimitiveType.Cylinder,
		Color.violet,
		transform => {
			transform.localScale = new Vector3(0.1f, 100, 0.1f);
			transform.localPosition = new Vector3(0, 100, 0);
		}
	);
	GameObject geographicNorthCrosshair = Utility.crosshair(
		PrimitiveType.Cylinder,
		Color.red,
		transform => {
			transform.localScale = new Vector3(0.1f, 100, 0.1f);
			transform.localPosition = new Vector3(0, 100, 0);
		}
	);
	GameObject planetaryNorthCrosshair = Utility.crosshair(
		PrimitiveType.Cylinder,
		Color.orange,
		transform => {
			transform.localScale = new Vector3(0.1f, 100, 0.1f);
			transform.localPosition = new Vector3(0, 100, 0);
		}
	);
	GameObject solarRotationCrosshair = Utility.crosshair(
		PrimitiveType.Cylinder,
		Color.yellow,
		transform => {
			transform.localScale = new Vector3(0.1f, 100, 0.1f);
			transform.localPosition = new Vector3(0, 100, 0);
		}
	);
	GameObject lunarRotationCrosshair = Utility.crosshair(
		PrimitiveType.Cylinder,
		Color.green,
		transform => {
			transform.localScale = new Vector3(0.1f, 100, 0.1f);
			transform.localPosition = new Vector3(0, 100, 0);
		}
	);
	GameObject sun = null!;
	GameObject moon = null!;
	public void Load() {
		Debug.Log("Sky.Load");

		sun = Icosphere.Create(3, 30);
		sun.layer = Layers.IgnoreRaycastMask;
		var sunMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
		sunMaterial.color = new Color(230 / 255f, 220 / 255f, 140 / 255f);
		sun.AddComponent<MeshRenderer>().material = sunMaterial;

		var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("CameraOverhaul.moon.jpg");
		var tex = new Texture2D(1, 1);
		var bytes = new byte[stream.Length];
		stream.Read(bytes);
		tex.LoadImage(bytes);
		stream.Dispose();

		moon = Icosphere.Create(4, 22.5f, Quaternion.Euler(0, 0, tiltAngle));
		moon.layer = Layers.IgnoreRaycastMask;
		var moonMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
		moonMaterial.color = new Color(230 / 255f, 220 / 255f, 200 / 255f);
		moonMaterial.mainTexture = tex;
		moon.AddComponent<MeshRenderer>().material = moonMaterial;
	}

	public void LateUpdateSingleton() {
		Render();
	}

	DayNightCycle dayNightCycle = (DayNightCycle) dayStageCycle._dayNightCycle;
	int tiltAngle = 30;
	int latitudeAngle = 50;
	// assume permanant summer solstice lol

	void Render() {
		var mapCenter = new Vector3(mapSize.TerrainSize.x * 0.5f, 10, mapSize.TerrainSize.y * 0.5f);
		var cameraCenter = /*mapCenter*/new Vector3(cam.position.x, 0, cam.position.z);

		var dayProgress = (
			dayNightCycle.DayNumber +
			dayNightCycle.FluidSecondsPassedToday / dayNightCycle.ConfiguredDayLengthInSeconds
		);
		//dayProgress *= 30;

		var solarAngle = (dayProgress + 3.5f / 24f) * 360f;

		var up = Quaternion.LookRotation(Vector3.up);
		upCrosshair.transform.localPosition = mapCenter;
		upCrosshair.transform.localRotation = up * Quaternion.Euler(90, 0, 0);

		var geographicNorth = Quaternion.LookRotation(Vector3.forward);
		geographicNorthCrosshair.transform.localPosition = mapCenter;
		geographicNorthCrosshair.transform.localRotation = geographicNorth * Quaternion.Euler(90, 0, 0);

		var planetaryNorth = geographicNorth * Quaternion.Euler(0 - latitudeAngle, 0, 0);
		planetaryNorthCrosshair.transform.localPosition = mapCenter;
		planetaryNorthCrosshair.transform.localRotation = planetaryNorth * Quaternion.Euler(90, 0, 0);

		var solarRotation = planetaryNorth * Quaternion.Euler(0, 0, solarAngle) * Quaternion.Euler(90 - tiltAngle, 0, 0);
		solarRotationCrosshair.transform.localPosition = mapCenter;
		solarRotationCrosshair.transform.localRotation = solarRotation * Quaternion.Euler(90, 0, 0);
		var sunVector = solarRotation * Vector3.forward;

		var lunarAngle = solarAngle * 29 / 28 + 180;
		//lunarAngle *= 3;
		var lunarRotation = planetaryNorth * Quaternion.Euler(0, 0, lunarAngle) * Quaternion.Euler(90, 0, 0);
		lunarRotationCrosshair.transform.localPosition = mapCenter;
		lunarRotationCrosshair.transform.localRotation = lunarRotation * Quaternion.Euler(90, 0, 0);
		var moonVector = lunarRotation * Vector3.forward;

		sun.transform.localRotation = solarRotation * Quaternion.Euler(0, 90, 0);
		sun.transform.localPosition = cameraCenter + sunVector * 800f;
		moon.transform.localPosition = cameraCenter + moonVector * 600f;
		moon.transform.localRotation = solarRotation * Quaternion.Euler(0, 0 - 90, 0);
		moon.GetComponent<MeshRenderer>().material.mainTextureOffset = (
			new Vector2((lunarAngle - solarAngle) / 360 + 0.5f, 0)
		);

		var transition = sunService._dayStageCycle.GetCurrentTransition();
		sunService.UpdateColors(transition);

		if (sunVector.y > 0) {
			var sunRelevance = Mathf.Clamp(sunVector.y * 10, 0, 1);
			sunService._sun.intensity *= sunRelevance;
			sunService._sun.transform.localRotation = Quaternion.LookRotation(Vector3.zero - sunVector);
		} else if (moonVector.y > 0) {
			var moonRelevance = (
				Vector3.Angle(sunVector, moonVector) / 180 *
				Mathf.Clamp(0 - sunVector.y * 10, 0, 1)
			);
			sunService._sun.intensity = moonRelevance * 0.5f;
			sunService._sun.transform.localRotation = Quaternion.LookRotation(Vector3.zero - moonVector);
			sunService._sun.color = Color.white;
		} else {
			sunService._sun.intensity = 0;
		}

		/*sunService._sun.transform.localRotation = Quaternion.LookRotation(Vector3.zero - sunVector(
			sunVector.y > 0 ?
			sunVector * 1200f : (
				moonVector.y > 0 ?
				moonVector * 1200f :
				Vector3.down * 1200f
			)
		)*/;

		//var percentX = inputService.MousePosition.x / Display.main.renderingWidth;
	}
}

[HarmonyPatch]
class SkyPatch {
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
	[HarmonyPrefix, HarmonyPatch(typeof(Sun), nameof(Sun.UpdateColorsAndRotation))]
	static bool UpdateColorsAndRotation() {
		return false;
	}
}
