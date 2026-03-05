using Godot;
using System;
using System.Collections.Generic;

namespace simplyRemadeNuxi.core;

/// <summary>
/// Helper class for calculating bend transformations based on MineImator's bend system.
///
/// MineImator bends work in two ways:
/// 1. **Mesh deformation**: At load time (and when bend angles change at runtime), the mesh
///    vertices are physically bent by segmenting the mesh along the bend axis and rotating
///    each segment. This is done by model_shape_generate_block/plane.
/// 2. **Child transform**: model_part_get_bend_matrix creates a transformation matrix that
///    positions child parts relative to the bent parent (lock_bend).
///
/// Coordinate system notes:
/// - MineImator uses GameMaker's coordinate system (X right, Y forward, Z up)
/// - Godot uses (X right, Y up, Z forward/back depending on convention)
/// - In the model JSON files, coordinates are in MineImator's space
/// - The MineImator axis mapping for bend: "x" -> MI X, "z" -> MI Y, "y" -> MI Z
/// </summary>
public static class BendHelper
{
	/// <summary>
	/// Enum representing which part of the model is bending.
	/// Maps to MineImator's e_part enum.
	/// </summary>
	public enum BendPart
	{
		None,
		Right,  // Bend along X axis
		Left,   // Bend along X axis
		Front,  // Bend along Y axis
		Back,   // Bend along Y axis
		Upper,  // Bend along Z axis
		Lower   // Bend along Z axis
	}

	/// <summary>
	/// Parsed bend configuration for a model part.
	/// Contains all the data needed to perform bend operations.
	/// </summary>
	public class BendConfig
	{
		public BendPart Part = BendPart.None;
		public float Offset;           // Position along the bend axis where the bend occurs (in pixels)
		public float? Size;            // Size of the bend zone (in pixels, null for default)
		public float EndOffset;        // End offset for IK
		public bool[] Axis = { false, false, false };  // Which axes are active [X, Y, Z]
		public float[] DirectionMin = { -180, -180, -180 };
		public float[] DirectionMax = { 180, 180, 180 };
		public bool[] Invert = { false, false, false };
		public float[] DefaultAngle = { 0, 0, 0 };    // Default/rest bend angle
		public bool InheritBend;
	}

	/// <summary>
	/// Gets the BendPart enum value from a string
	/// </summary>
	public static BendPart ParseBendPart(string part)
	{
		return part?.ToLower() switch
		{
			"right" => BendPart.Right,
			"left" => BendPart.Left,
			"front" => BendPart.Front,
			"back" => BendPart.Back,
			"upper" => BendPart.Upper,
			"lower" => BendPart.Lower,
			_ => BendPart.None
		};
	}

	/// <summary>
	/// Parses a MiBend JSON object into a BendConfig.
	/// Handles axis mapping, direction limits, invert flags, etc.
	/// </summary>
	public static BendConfig ParseBendConfig(MiBend bend, float[] partScale = null)
	{
		if (bend == null || !bend.Offset.HasValue || string.IsNullOrEmpty(bend.Part))
			return null;

		var config = new BendConfig();
		config.Part = ParseBendPart(bend.Part);
		if (config.Part == BendPart.None)
			return null;

		config.Offset = bend.Offset.Value;
		config.Size = bend.Size;
		config.EndOffset = bend.EndOffset ?? 0;
		config.InheritBend = (bend.InheritBend ?? 0) != 0;

		// Apply part scale to offset and size (matching MineImator's model_file_load_part)
		float scaleOnAxis = 1.0f;
		if (partScale != null && partScale.Length >= 3)
		{
			int scaleIdx = GetSegmentAxis(config.Part);
			if (scaleIdx >= 0 && scaleIdx < partScale.Length)
				scaleOnAxis = partScale[scaleIdx];
		}
		config.Offset *= scaleOnAxis;
		if (config.Size.HasValue)
			config.Size *= scaleOnAxis;

		// Parse axis flags
		// MineImator axis mapping: "x" -> X(0), "z" -> Y(1), "y" -> Z(2)
		var (axisX, axisY, axisZ) = ParseBendAxis(bend.Axis);
		config.Axis = new[] { axisX, axisY, axisZ };

		// Parse direction limits
		if (bend.DirectionMin != null)
		{
			// The direction_min values correspond to the active axes in order
			var activeAxes = GetActiveAxisIndices(config.Axis);
			for (int i = 0; i < Math.Min(bend.DirectionMin.Length, activeAxes.Count); i++)
				config.DirectionMin[activeAxes[i]] = bend.DirectionMin[i];
		}
		if (bend.DirectionMax != null)
		{
			var activeAxes = GetActiveAxisIndices(config.Axis);
			for (int i = 0; i < Math.Min(bend.DirectionMax.Length, activeAxes.Count); i++)
				config.DirectionMax[activeAxes[i]] = bend.DirectionMax[i];
		}

		// Parse invert flags
		if (bend.Invert != null)
		{
			var activeAxes = GetActiveAxisIndices(config.Axis);
			if (bend.Invert.Length == 1 && activeAxes.Count == 1)
			{
				config.Invert[activeAxes[0]] = bend.Invert[0];
			}
			else
			{
				for (int i = 0; i < Math.Min(bend.Invert.Length, activeAxes.Count); i++)
					config.Invert[activeAxes[i]] = bend.Invert[i];
			}
		}

		// Parse default angle
		if (bend.Angle != null)
		{
			var activeAxes = GetActiveAxisIndices(config.Axis);
			if (bend.Angle.Length == 1 && activeAxes.Count == 1)
			{
				config.DefaultAngle[activeAxes[0]] = bend.Angle[0];
			}
			else
			{
				for (int i = 0; i < Math.Min(bend.Angle.Length, activeAxes.Count); i++)
					config.DefaultAngle[activeAxes[i]] = bend.Angle[i];
			}
		}

		return config;
	}

	/// <summary>
	/// Gets the segment axis index (0=X, 1=Y, 2=Z) that the mesh should be split along for bending.
	/// </summary>
	public static int GetSegmentAxis(BendPart bendPart)
	{
		return bendPart switch
		{
			BendPart.Right or BendPart.Left => 0,   // Split along X
			BendPart.Front or BendPart.Back => 1,   // Split along Y
			BendPart.Upper or BendPart.Lower => 2,  // Split along Z
			_ => -1
		};
	}

	/// <summary>
	/// Returns true if the bend angle should be inverted for this bend part.
	/// In MineImator: invangle = (bend_part = e_part.LOWER || bend_part = e_part.BACK || bend_part = e_part.LEFT)
	/// </summary>
	public static bool ShouldInvertAngle(BendPart bendPart)
	{
		return bendPart == BendPart.Lower || bendPart == BendPart.Back || bendPart == BendPart.Left;
	}

	/// <summary>
	/// Computes the bend transformation matrix for child parts locked to the bent half.
	/// This replicates MineImator's model_part_get_bend_matrix.
	///
	/// In MineImator, this function:
	/// 1. Clamps bend angles to direction_min/max
	/// 2. Applies invert flags
	/// 3. Zeros out non-active axes
	/// 4. Sets pivot position based on bend_part and bend_offset
	/// 5. Creates matrix_build(pos, bend, scale) = translate then rotate then scale
	///
	/// This is used for positioning child parts that are "locked to bend half".
	/// </summary>
	/// <param name="config">The bend configuration</param>
	/// <param name="bendAngles">Raw bend angles in degrees (before clamping/inversion)</param>
	/// <param name="position">Additional position offset (usually zero)</param>
	/// <param name="scale">Scale factor (default 1,1,1)</param>
	/// <returns>The bend transformation matrix</returns>
	public static Transform3D GetBendPartMatrix(BendConfig config, Vector3 bendAngles, Vector3 position, Vector3? scale = null)
	{
		if (config == null || config.Part == BendPart.None)
			return Transform3D.Identity;

		scale ??= Vector3.One;

		// Process bend angles: clamp, invert, zero non-active
		float[] bend = { bendAngles.X, bendAngles.Y, bendAngles.Z };
		for (int i = 0; i < 3; i++)
		{
			if (bend[i] == 0) continue;

			// Clamp to direction limits
			bend[i] = Mathf.Clamp(bend[i], config.DirectionMin[i], config.DirectionMax[i]);

			// Apply invert
			if (config.Invert[i])
				bend[i] *= -1;

			// Zero out non-active axes
			if (!config.Axis[i])
				bend[i] = 0;
		}

		// Set pivot position based on bend part
		Vector3 pos = position;
		float bendOffsetGodot = config.Offset / 16.0f; // Convert pixels to Godot units
		switch (config.Part)
		{
			case BendPart.Right:
			case BendPart.Left:
				pos.X = bendOffsetGodot;
				break;
			case BendPart.Front:
			case BendPart.Back:
				pos.Y = bendOffsetGodot;
				break;
			case BendPart.Upper:
			case BendPart.Lower:
				pos.Z = bendOffsetGodot;
				break;
		}

		// Create matrix: translate to pivot, then rotate by bend angles, then scale
		// GameMaker's matrix_build(px, py, pz, rx, ry, rz, sx, sy, sz) =
		//   Scale * RotZ * RotY * RotX * Translate
		// In Godot terms: first translate, then rotate XYZ, then scale
		Vector3 bendRad = new Vector3(
			Mathf.DegToRad(bend[0]),
			Mathf.DegToRad(bend[1]),
			Mathf.DegToRad(bend[2])
		);

		// Build rotation basis (XYZ Euler order matching GameMaker)
		Basis rotBasis = Basis.FromEuler(bendRad);

		// Apply scale
		rotBasis = rotBasis.Scaled(scale.Value);

		// The transform: origin is the pivot, basis handles rotation+scale
		return new Transform3D(rotBasis, pos);
	}

	/// <summary>
	/// Calculates the bend weight (0-1) for a given segment position within the bend zone.
	/// Weight 0 = no bend applied, weight 1 = full bend applied.
	///
	/// Matches MineImator's segment weight calculation in model_shape_generate_block.
	/// </summary>
	public static float CalculateBendWeight(float segmentPos, float bendStart, float bendEnd, float bendSize, BendPart bendPart)
	{
		float weight;

		if (segmentPos < bendStart)
		{
			// Below/before bend zone - no angle
			weight = 0.0f;
		}
		else if (segmentPos >= bendEnd)
		{
			// Above/after bend zone - full angle
			weight = 1.0f;
		}
		else
		{
			// Inside bend zone - partial angle based on position
			weight = 1.0f - (bendEnd - segmentPos) / bendSize;
		}

		// Invert weight for lower/back/left parts
		if (ShouldInvertAngle(bendPart))
		{
			weight = 1.0f - weight;
		}

		return weight;
	}

	/// <summary>
	/// Gets the bend vector with easing applied.
	/// Matches model_shape_get_bend from MineImator:
	///   X uses ease-in-out-quint
	///   Y uses ease-in-out-quint
	///   Z uses linear weight
	/// </summary>
	public static Vector3 GetBendVector(Vector3 bendAngles, float weight)
	{
		return new Vector3(
			bendAngles.X * EaseInOutQuint(weight),
			bendAngles.Y * EaseInOutQuint(weight),
			bendAngles.Z * weight
		);
	}

	/// <summary>
	/// Creates the bend transformation matrix for a mesh segment.
	/// This replicates model_part_get_bend_matrix as called from model_shape_generate_block
	/// for mesh vertex deformation.
	///
	/// Unlike GetBendPartMatrix (which is for child part positioning), this version:
	/// - Takes pre-computed eased bend angles
	/// - Uses the bend offset as pivot
	/// - Optionally subtracts shape position for shape-level transforms
	/// </summary>
	/// <param name="config">The bend configuration</param>
	/// <param name="easedBendAngles">Pre-computed eased bend angles (from GetBendVector)</param>
	/// <param name="scale">Scale to apply (used for Z-fighting prevention)</param>
	/// <param name="shapePosition">Shape's local position (for shape-level offset, null for part-level)</param>
	/// <param name="shapeRotation">Shape's local rotation in degrees (for shape-level rotation, null for none)</param>
	/// <returns>The transformation matrix for the mesh segment</returns>
	public static Transform3D GetShapeBendMatrix(
		BendConfig config,
		Vector3 easedBendAngles,
		Vector3 scale,
		Vector3? shapePosition = null,
		Vector3? shapeRotation = null)
	{
		if (config == null || config.Part == BendPart.None)
			return Transform3D.Identity;

		// Process bend angles through config (clamp, invert, zero non-active)
		float[] bend = { easedBendAngles.X, easedBendAngles.Y, easedBendAngles.Z };
		for (int i = 0; i < 3; i++)
		{
			if (bend[i] == 0) continue;
			bend[i] = Mathf.Clamp(bend[i], config.DirectionMin[i], config.DirectionMax[i]);
			if (config.Invert[i]) bend[i] *= -1;
			if (!config.Axis[i]) bend[i] = 0;
		}

		// Get pivot position
		float bendOffsetGodot = config.Offset / 16.0f;
		Vector3 pos = Vector3.Zero;
		switch (config.Part)
		{
			case BendPart.Right:
			case BendPart.Left:
				pos.X = bendOffsetGodot;
				if (shapePosition.HasValue)
					pos.X -= shapePosition.Value.X;
				break;
			case BendPart.Front:
			case BendPart.Back:
				pos.Y = bendOffsetGodot;
				if (shapePosition.HasValue)
					pos.Y -= shapePosition.Value.Y;
				break;
			case BendPart.Upper:
			case BendPart.Lower:
				pos.Z = bendOffsetGodot;
				if (shapePosition.HasValue)
					pos.Z -= shapePosition.Value.Z;
				break;
		}

		// Build the main bend matrix
		Vector3 bendRad = new Vector3(
			Mathf.DegToRad(bend[0]),
			Mathf.DegToRad(bend[1]),
			Mathf.DegToRad(bend[2])
		);

		Basis rotBasis = Basis.FromEuler(bendRad);
		rotBasis = rotBasis.Scaled(scale);
		Transform3D mat = new Transform3D(rotBasis, pos);

		// For shapes, also apply inverse translation + shape rotation
		// This matches: mat = matrix_multiply(matrix_build(-pos, rotation, 1), mat)
		if (shapePosition.HasValue && shapeRotation.HasValue)
		{
			Vector3 rotRad = new Vector3(
				Mathf.DegToRad(shapeRotation.Value.X),
				Mathf.DegToRad(shapeRotation.Value.Y),
				Mathf.DegToRad(shapeRotation.Value.Z)
			);
			Basis shapeBasis = Basis.FromEuler(rotRad);
			Transform3D shapeMat = new Transform3D(shapeBasis, -pos);
			mat = shapeMat * mat;
		}

		return mat;
	}

	/// <summary>
	/// Calculates the bend zone start and end positions for a shape.
	/// bendstart = (bend_offset - (position[axis] + from[axis])) - bendsize / 2
	/// bendend = (bend_offset - (position[axis] + from[axis])) + bendsize / 2
	/// </summary>
	/// <param name="bendOffsetGodot">Bend offset in Godot units (already converted from pixels)</param>
	/// <param name="bendSizeGodot">Bend size in Godot units (null for default)</param>
	/// <param name="shapeFromAlongAxis">Shape's start position along the segment axis in Godot units</param>
	/// <param name="realistic">Whether to use realistic (smooth) or blocky bending</param>
	/// <returns>Tuple of (bendStart, bendEnd, actualBendSize)</returns>
	public static (float bendStart, float bendEnd, float bendSize) GetBendZone(
		float bendOffsetGodot, float? bendSizeGodot, float shapeFromAlongAxis, bool realistic = true)
	{
		// Default bend size: 4 pixels for realistic, 1 pixel for blocky (converted to Godot units)
		float size = bendSizeGodot ?? (realistic ? 4.0f / 16.0f : 1.0f / 16.0f);
		float bendStart = (bendOffsetGodot - shapeFromAlongAxis) - size / 2.0f;
		float bendEnd = (bendOffsetGodot - shapeFromAlongAxis) + size / 2.0f;
		return (bendStart, bendEnd, size);
	}

	/// <summary>
	/// Gets the number of segments to use for bending based on bend size and style.
	/// </summary>
	public static int GetBendDetail(float? bendSizeGodot, bool realistic, float scale = 1.0f)
	{
		float size = bendSizeGodot ?? (realistic ? 4.0f / 16.0f : 1.0f / 16.0f);
		// Convert back to pixels for detail calculation
		float sizeInPixels = size * 16.0f;
		int detail = realistic ? (int)Math.Max(sizeInPixels, 2) : 2;

		// Reduce detail for scaled shapes
		if (bendSizeGodot.HasValue && bendSizeGodot.Value >= 1.0f / 16.0f && scale > 0.5f)
		{
			detail = (int)(detail / scale);
		}

		return Math.Max(detail, 2);
	}

	/// <summary>
	/// Parses the axis string/array from MiBend into boolean flags.
	/// MineImator axis mapping: "x" = MI X axis, "z" = MI Y axis, "y" = MI Z axis
	/// </summary>
	public static (bool x, bool y, bool z) ParseBendAxis(object axis)
	{
		bool x = false, y = false, z = false;

		if (axis == null)
			return (false, false, false);

		if (axis is string axisStr)
		{
			switch (axisStr.ToLower())
			{
				case "x": x = true; break;
				case "z": y = true; break; // MineImator "z" -> internal Y
				case "y": z = true; break; // MineImator "y" -> internal Z
			}
		}
		else if (axis is System.Text.Json.JsonElement jsonElement)
		{
			if (jsonElement.ValueKind == System.Text.Json.JsonValueKind.String)
			{
				string str = jsonElement.GetString();
				switch (str?.ToLower())
				{
					case "x": x = true; break;
					case "z": y = true; break;
					case "y": z = true; break;
				}
			}
			else if (jsonElement.ValueKind == System.Text.Json.JsonValueKind.Array)
			{
				foreach (var item in jsonElement.EnumerateArray())
				{
					if (item.ValueKind == System.Text.Json.JsonValueKind.String)
					{
						string str = item.GetString();
						switch (str?.ToLower())
						{
							case "x": x = true; break;
							case "z": y = true; break;
							case "y": z = true; break;
						}
					}
				}
			}
		}

		return (x, y, z);
	}

	/// <summary>
	/// Gets the indices of active axes from the axis flags array.
	/// </summary>
	public static List<int> GetActiveAxisIndices(bool[] axis)
	{
		var result = new List<int>();
		for (int i = 0; i < axis.Length; i++)
		{
			if (axis[i]) result.Add(i);
		}
		return result;
	}

	/// <summary>
	/// Gets the pivot position for a bend based on the bend part.
	/// </summary>
	public static Vector3 GetPivotForBendPart(BendPart bendPart, float bendOffsetGodot)
	{
		return bendPart switch
		{
			BendPart.Right or BendPart.Left => new Vector3(bendOffsetGodot, 0, 0),
			BendPart.Front or BendPart.Back => new Vector3(0, bendOffsetGodot, 0),
			BendPart.Upper or BendPart.Lower => new Vector3(0, 0, bendOffsetGodot),
			_ => Vector3.Zero
		};
	}

	/// <summary>
	/// Calculates scale adjustment for blocky bending to prevent pinching.
	/// Matches model_shape_get_bend_scale from MineImator.
	/// </summary>
	public static Vector3 GetBlockyBendScale(float bendStart, float bendEnd, float weight, float segmentPos, float bendAngle, int bendAxis)
	{
		if (segmentPos <= bendStart || segmentPos >= bendEnd)
			return Vector3.Zero;

		float scale;
		if (weight <= 0.5f)
			scale = weight * 2.0f;
		else
			scale = (1.0f - weight) * 2.0f;

		// Calculate angle percentage (0-90 degrees maps to 0-1)
		float absAngle = Math.Abs(bendAngle);
		if (absAngle > 90.0f)
			absAngle -= (absAngle - 90.0f) * 2.0f;

		float anglePercent = Mathf.Clamp(absAngle / 90.0f, 0.0f, 1.0f);
		scale *= anglePercent;
		scale = EaseInCubic(scale);
		scale /= 2.5f;

		// Don't scale along the bend axis
		Vector3 result = new Vector3(scale, scale, scale);
		result[bendAxis] = 0;

		return result;
	}

	/// <summary>
	/// Easing function: ease-in-out quintic.
	/// Matches MineImator's ease("easeinoutquint", t).
	/// </summary>
	public static float EaseInOutQuint(float t)
	{
		if (t < 0.5f)
		{
			return 16.0f * t * t * t * t * t;
		}
		else
		{
			float t2 = 2.0f * t - 2.0f;
			return 1.0f - t2 * t2 * t2 * t2 * t2 / 2.0f;
		}
	}

	/// <summary>
	/// Easing function: ease-in cubic.
	/// Matches MineImator's ease("easeincubic", t).
	/// </summary>
	public static float EaseInCubic(float t)
	{
		return t * t * t;
	}

	/// <summary>
	/// Transforms a point by a bend matrix.
	/// </summary>
	public static Vector3 TransformPoint(Vector3 point, Transform3D matrix)
	{
		return matrix * point;
	}
}
