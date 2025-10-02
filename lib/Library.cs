using UnityEngine;

class Icosphere {
	public static GameObject Create(int recursionLevel, float radius = 1f, Quaternion? maybeTextureTilt = null) {
		Quaternion textureTilt = maybeTextureTilt ?? Quaternion.identity;

		Vector3[] verts;
		int[] tris;
		Vector2[] uvs;

		// create icosahedron
		float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
		var vList = new System.Collections.Generic.List<Vector3> {
			new Vector3(-1,  t,  0),
			new Vector3( 1,  t,  0),
			new Vector3(-1, -t,  0),
			new Vector3( 1, -t,  0),
			new Vector3( 0, -1,  t),
			new Vector3( 0,  1,  t),
			new Vector3( 0, -1, -t),
			new Vector3( 0,  1, -t),
			new Vector3( t,  0, -1),
			new Vector3( t,  0,  1),
			new Vector3(-t,  0, -1),
			new Vector3(-t,  0,  1)
		};
		for (int i = 0; i < vList.Count; i++) vList[i] = vList[i].normalized * radius;

		var faces = new System.Collections.Generic.List<int[]>
		{
			new[]{0,11,5}, new[]{0,5,1}, new[]{0,1,7}, new[]{0,7,10}, new[]{0,10,11},
			new[]{1,5,9}, new[]{5,11,4}, new[]{11,10,2}, new[]{10,7,6}, new[]{7,1,8},
			new[]{3,9,4}, new[]{3,4,2}, new[]{3,2,6}, new[]{3,6,8}, new[]{3,8,9},
			new[]{4,9,5}, new[]{2,4,11}, new[]{6,2,10}, new[]{8,6,7}, new[]{9,8,1}
		};

		var midpointCache = new System.Collections.Generic.Dictionary<long, int>();

		int GetMidpoint(int a, int b)
		{
			long key = ((long)System.Math.Min(a, b) << 32) | (long)System.Math.Max(a, b);
			if (midpointCache.TryGetValue(key, out int idx)) return idx;
			Vector3 m = ((vList[a] + vList[b]) * 0.5f).normalized * radius;
			idx = vList.Count;
			vList.Add(m);
			midpointCache[key] = idx;
			return idx;
		}

		for (int i = 0; i < recursionLevel; i++)
		{
			var newFaces = new System.Collections.Generic.List<int[]>();
			foreach (var f in faces)
			{
				int a = GetMidpoint(f[0], f[1]);
				int b = GetMidpoint(f[1], f[2]);
				int c = GetMidpoint(f[2], f[0]);
				newFaces.Add(new[]{f[0], a, c});
				newFaces.Add(new[]{f[1], b, a});
				newFaces.Add(new[]{f[2], c, b});
				newFaces.Add(new[]{a, b, c});
			}
			faces = newFaces;
		}

		verts = vList.ToArray();

		var keptFaces = new System.Collections.Generic.List<int[]>();
		foreach (var f in faces)
		{
			if (vList[f[0]].x < 0.01f || vList[f[1]].x < 0.01f || vList[f[2]].x < 0.01f)
				continue;
			keptFaces.Add(f);
		}

		tris = new int[keptFaces.Count * 3];
		for (int i = 0; i < keptFaces.Count; i++)
		{
			tris[i * 3 + 0] = keptFaces[i][0];
			tris[i * 3 + 1] = keptFaces[i][1];
			tris[i * 3 + 2] = keptFaces[i][2];
		}

		uvs = new Vector2[verts.Length];
		for (int i = 0; i < verts.Length; i++)
		{
			Vector3 n = verts[i].normalized;

			// rotate the direction vector in 3D to tilt the texture mapping
			Vector3 nTilted = textureTilt * n;

			float u = 0.5f + (Mathf.Atan2(nTilted.z, nTilted.x) / (2f * Mathf.PI));
			float v = (Mathf.Asin(nTilted.y) / Mathf.PI) - 0.5f;
			uvs[i] = new Vector2(u, v);
		}

		var go = new GameObject();
		var meshFilter = go.AddComponent<MeshFilter>();
		var mesh = new Mesh();
		mesh.vertices = verts;
		mesh.triangles = tris;
		mesh.uv = uvs;
		mesh.RecalculateNormals();
		mesh.RecalculateBounds();
		meshFilter.sharedMesh = mesh;
		return go;
	}
}

