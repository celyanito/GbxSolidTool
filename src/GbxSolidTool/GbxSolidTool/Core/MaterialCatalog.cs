using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace GbxSolidTool.Core;

public static class MaterialCatalog
{
	public static readonly HashSet<string> KnownMaterials = new(StringComparer.OrdinalIgnoreCase)
	{
		"Concrete","Pavement","Grass","Ice","Metal","Sand","Dirt","Turbo","DirtRoad","Rubber",
		"SlidingRubber","Test","Rock","Water","Wood","Danger","Asphalt","WetDirtRoad","WetAsphalt",
		"WetPavement","WetGrass","Snow","ResonantMetal","GolfBall","GolfWall","GolfGround",
		"Turbo2","Bumper","NotCollidable","FreeWheeling","TurboRoulette",
	};

	public static bool IsKnown(string name) => KnownMaterials.Contains(name);

	public static Color GetMaterialColor(string name)
	{
		return name.ToLowerInvariant() switch
		{
			"metal" => Colors.LightGray,
			"resonantmetal" => Colors.WhiteSmoke,

			"sand" => Color.FromRgb(214, 158, 46),
			"dirt" => Color.FromRgb(130, 84, 42),
			"dirtroad" => Color.FromRgb(150, 110, 70),
			"wetdirtroad" => Color.FromRgb(110, 80, 60),

			"grass" => Colors.LimeGreen,
			"wetgrass" => Colors.SeaGreen,
			"snow" => Colors.AliceBlue,
			"ice" => Colors.LightBlue,

			"water" => Colors.DodgerBlue,

			"asphalt" => Colors.DimGray,
			"wetasphalt" => Colors.SlateGray,
			"pavement" => Colors.Gray,
			"wetpavement" => Colors.DarkGray,
			"concrete" => Colors.LightSlateGray,

			"wood" => Color.FromRgb(153, 101, 64),
			"rock" => Colors.DarkSlateGray,

			"rubber" => Colors.MediumPurple,
			"slidingrubber" => Colors.HotPink,

			"turbo" => Colors.OrangeRed,
			"turbo2" => Colors.Orange,
			"turboroulette" => Colors.Gold,

			"danger" => Colors.Red,
			"bumper" => Colors.Cyan,

			"notcollidable" => Colors.DarkRed,
			"freewheeling" => Colors.Khaki,

			"test" => Colors.Magenta,

			"golfball" => Colors.White,
			"golfwall" => Colors.LightSteelBlue,
			"golfground" => Colors.PaleGreen,

			_ => Colors.Red
		};
	}
}
