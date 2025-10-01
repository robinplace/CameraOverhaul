class Icosphere {
	public static GameObject Create(int recursionLevel, float radius = 1f) {
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
		tris = new int[faces.Count * 3];
		for (int i = 0; i < faces.Count; i++)
		{
			tris[i * 3 + 0] = faces[i][0];
			tris[i * 3 + 1] = faces[i][1];
			tris[i * 3 + 2] = faces[i][2];
		}

		uvs = new Vector2[verts.Length];
		for (int i = 0; i < verts.Length; i++)
		{
			Vector3 n = verts[i].normalized;
			float u = 0.5f + (Mathf.Atan2(n.z, n.x) / (2f * Mathf.PI));
			float v = 0.5f - (Mathf.Asin(n.y) / Mathf.PI);
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

