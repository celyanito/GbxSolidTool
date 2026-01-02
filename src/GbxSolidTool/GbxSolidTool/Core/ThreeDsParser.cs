using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using GbxSolidTool.Models;

namespace GbxSolidTool.Core;

public static class ThreeDsParser
{
	public static List<string> ExtractMaterialNamesA000(string path)
	{
		var bytes = File.ReadAllBytes(path);
		var names = new List<string>();

		ushort U16(int o) => (ushort)(bytes[o] | (bytes[o + 1] << 8));
		uint U32(int o) => (uint)(bytes[o] | (bytes[o + 1] << 8) | (bytes[o + 2] << 16) | (bytes[o + 3] << 24));

		string ReadZString(int start, int end)
		{
			int z = start;
			while (z < end && bytes[z] != 0) z++;
			return Encoding.ASCII.GetString(bytes, start, Math.Max(0, z - start));
		}

		void Walk(int start, int end)
		{
			int o = start;
			while (o + 6 <= end)
			{
				ushort cid = U16(o);
				uint len = U32(o + 2);
				if (len < 6 || o + len > end) break;

				int content = o + 6;
				int chunkEnd = o + (int)len;

				if (cid == 0xA000) // material name
				{
					var n = ReadZString(content, chunkEnd);
					if (!string.IsNullOrWhiteSpace(n))
						names.Add(n);
				}

				// containers
				if (cid == 0x4D4D || cid == 0x3D3D || cid == 0xAFFF || cid == 0x4100)
				{
					Walk(content, chunkEnd);
				}
				else if (cid == 0x4000) // object block (name + subchunks)
				{
					int z = content;
					while (z < chunkEnd && bytes[z] != 0) z++;
					if (z + 1 < chunkEnd)
						Walk(z + 1, chunkEnd);
				}

				o += (int)len;
			}
		}

		Walk(0, bytes.Length);

		return names
			.Where(s => !string.IsNullOrWhiteSpace(s))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	public static List<FaceMatGroup> ExtractFaceMaterialGroups4130(string path)
	{
		var bytes = File.ReadAllBytes(path);
		var groups = new List<FaceMatGroup>();

		ushort U16(int o) => (ushort)(bytes[o] | (bytes[o + 1] << 8));
		uint U32(int o) => (uint)(bytes[o] | (bytes[o + 1] << 8) | (bytes[o + 2] << 16) | (bytes[o + 3] << 24));

		string ReadZString(ref int o, int end)
		{
			int start = o;
			while (o < end && bytes[o] != 0) o++;
			var s = Encoding.ASCII.GetString(bytes, start, Math.Max(0, o - start));
			if (o < end) o++; // skip \0
			return s;
		}

		void Walk(int start, int end)
		{
			int o = start;
			while (o + 6 <= end)
			{
				ushort cid = U16(o);
				int len = (int)U32(o + 2);
				if (len < 6 || o + len > end) break;

				int content = o + 6;
				int chunkEnd = o + len;

				switch (cid)
				{
					case 0x4D4D: // Main
					case 0x3D3D: // 3D Editor
					case 0x4100: // TriMesh container
						Walk(content, chunkEnd);
						break;

					case 0x4000: // Object block: name(string) + subchunks
						{
							int p = content;
							while (p < chunkEnd && bytes[p] != 0) p++;
							if (p < chunkEnd) p++; // skip \0
							if (p < chunkEnd) Walk(p, chunkEnd);
							break;
						}

					case 0x4120: // Faces + subchunks (dont 0x4130)
						{
							if (content + 2 > chunkEnd) break;
							int faceCount = U16(content);
							int faceRecordsBytes = 2 + faceCount * 8;
							int subStart = content + faceRecordsBytes;
							if (subStart < chunkEnd)
								Walk(subStart, chunkEnd);
							break;
						}

					case 0x4130: // Face Material group
						{
							int t = content;
							string matName = ReadZString(ref t, chunkEnd);

							int faceCount = 0;
							if (t + 2 <= chunkEnd)
								faceCount = U16(t);

							groups.Add(new FaceMatGroup { MaterialName = matName, FaceCount = faceCount });
							break;
						}
				}

				o += len;
			}
		}

		Walk(0, bytes.Length);
		return groups;
	}
}
