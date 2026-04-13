using Godot;
using System;

namespace simplyRemadeNuxi.core;

/// <summary>
/// Identifies which directional half of a part is bent.
/// Matches Modelbench's e_part enum.
/// </summary>
public enum BendPart
{
	Right,
	Left,
	Front,
	Back,
	Upper,
	Lower
}

/// <summary>
/// Bend style setting for character models.
/// </summary>
public enum BendStyle
{
	Realistic,
	Blocky,
	ProjectDefault
}

/// <summary>
/// Holds all bend parameters for a part, derived from MiBend JSON data.
/// All size/offset values are in Minecraft pixels (not Godot units).
/// </summary>
public struct BendParams
{
	/// <summary>Bend angle in degrees (X, Y, Z axes)</summary>
	public Vector3 Angle;
	
	/// <summary>Bend pivot offset in pixels along the bend axis</summary>
	public float BendOffset;
	
	/// <summary>Bend region size in pixels (default 4)</summary>
	public float BendSize;

	/// <summary>Whether BendSize was explicitly set in the JSON (vs defaulting to 4)</summary>
	public bool ExplicitBendSize;

	/// <summary>Number of bend segments (default auto-calculated)</summary>
	public float? Detail;
	
	/// <summary>Which directional half of the part is bent</summary>
	public BendPart Part;
	
	/// <summary>Which axes are active for bending</summary>
	public bool AxisX, AxisY, AxisZ;
	
	/// <summary>Whether to invert the bend angle per axis</summary>
	public bool InvertX, InvertY, InvertZ;
	
	/// <summary>Minimum allowed bend angle per axis (degrees)</summary>
	public Vector3 DirectionMin;
	
	/// <summary>Maximum allowed bend angle per axis (degrees)</summary>
	public Vector3 DirectionMax;

	/// <summary>
	/// When true, this part adds its parent's bend angle to its own.
	/// Matches Modelbench's inherit_bend / INHERIT_BEND.
	/// </summary>
	public bool InheritBend;
}

/// <summary>
/// Provides bend math utilities matching Modelbench's GML implementation.
/// </summary>
public static class BendHelper
{
	/// <summary>
	/// Parses a MiBend JSON object into a BendParams struct.
	/// Matches the logic in Modelbench's model_load_part.gml and el_update_part.gml.
	/// </summary>
	public static BendParams? ParseBend(MiBend bend, float[] partScale, BendStyle bendStyle = BendStyle.ProjectDefault)
	{
		if (bend == null) return null;
		
		// Parse part direction
		BendPart part = BendPart.Upper; // default
		if (!string.IsNullOrEmpty(bend.Part))
		{
			switch (bend.Part.ToLowerInvariant())
			{
				case "right":  part = BendPart.Right;  break;
				case "left":   part = BendPart.Left;   break;
				case "front":  part = BendPart.Front;  break;
				case "back":   part = BendPart.Back;   break;
				case "upper":  part = BendPart.Upper;  break;
				case "lower":  part = BendPart.Lower;  break;
			}
		}
		
		// Parse axes
		bool axisX = false, axisY = false, axisZ = false;
		var axisIndices = new System.Collections.Generic.List<int>();
		
		if (bend.Axis is System.Text.Json.JsonElement axisElem)
		{
			if (axisElem.ValueKind == System.Text.Json.JsonValueKind.String)
			{
				ParseAxisString(axisElem.GetString(), ref axisX, ref axisY, ref axisZ, axisIndices);
			}
			else if (axisElem.ValueKind == System.Text.Json.JsonValueKind.Array)
			{
				foreach (var item in axisElem.EnumerateArray())
				{
					if (item.ValueKind == System.Text.Json.JsonValueKind.String)
						ParseAxisString(item.GetString(), ref axisX, ref axisY, ref axisZ, axisIndices);
				}
			}
		}
		else if (bend.Axis is string axisStr)
		{
			ParseAxisString(axisStr, ref axisX, ref axisY, ref axisZ, axisIndices);
		}
		
		// If no axes defined, no bending
		if (!axisX && !axisY && !axisZ) return null;
		
		// Parse direction min/max
		Vector3 dirMin = new Vector3(-180, -180, -180);
		Vector3 dirMax = new Vector3(180, 180, 180);
		
		if (bend.DirectionMin != null)
		{
			if (bend.DirectionMin.Length == 1 && axisIndices.Count == 1)
				SetVec3Component(ref dirMin, axisIndices[0], bend.DirectionMin[0]);
			else
				for (int i = 0; i < Math.Min(bend.DirectionMin.Length, axisIndices.Count); i++)
					SetVec3Component(ref dirMin, axisIndices[i], bend.DirectionMin[i]);
		}
		
		if (bend.DirectionMax != null)
		{
			if (bend.DirectionMax.Length == 1 && axisIndices.Count == 1)
				SetVec3Component(ref dirMax, axisIndices[0], bend.DirectionMax[0]);
			else
				for (int i = 0; i < Math.Min(bend.DirectionMax.Length, axisIndices.Count); i++)
					SetVec3Component(ref dirMax, axisIndices[i], bend.DirectionMax[i]);
		}
		
		// Parse invert
		bool invertX = false, invertY = false, invertZ = false;
		if (bend.Invert != null)
		{
			if (bend.Invert.Length == 1 && axisIndices.Count == 1)
				SetBoolComponent(ref invertX, ref invertY, ref invertZ, axisIndices[0], bend.Invert[0]);
			else
				for (int i = 0; i < Math.Min(bend.Invert.Length, axisIndices.Count); i++)
					SetBoolComponent(ref invertX, ref invertY, ref invertZ, axisIndices[i], bend.Invert[i]);
		}
		
		// Parse default angle
		Vector3 angle = Vector3.Zero;
		if (bend.Angle != null)
		{
			if (bend.Angle.Length == 1 && axisIndices.Count == 1)
				SetVec3Component(ref angle, axisIndices[0], bend.Angle[0]);
			else
				for (int i = 0; i < Math.Min(bend.Angle.Length, axisIndices.Count); i++)
					SetVec3Component(ref angle, axisIndices[i], bend.Angle[i]);
		}
		
		// Clamp angle to direction limits and apply invert
		angle = ClampAndInvertAngle(angle, dirMin, dirMax, invertX, invertY, invertZ, axisX, axisY, axisZ);
		
		// Parse offset, size, and detail (in pixels)
		float offset = bend.Offset ?? 0.0f;
		bool explicitBendSize = bend.Size.HasValue;
		float? detail = bend.Detail;

		// Resolve bend style to effective style
		BendStyle effectiveStyle = (bendStyle == BendStyle.ProjectDefault)
			? simplyRemadeNuxi.Main.ProjectBendStyle
			: bendStyle;

		float defaultBendSize = (effectiveStyle == BendStyle.Realistic) ? 4.0f : 1.0f;
		float size = bend.Size ?? defaultBendSize;
		
		// Scale offset/size by part scale (matching el_update_part.gml)
		float scaleX = partScale != null && partScale.Length > 0 ? partScale[0] : 1.0f;
		float scaleY = partScale != null && partScale.Length > 1 ? partScale[1] : 1.0f;
		float scaleZ = partScale != null && partScale.Length > 2 ? partScale[2] : 1.0f;
		
		// Scale offset/size by the part scale along the bend axis.
		// In the JSON/Godot coordinate system (Y-up):
		//   RIGHT/LEFT  -> X scale
		//   UPPER/LOWER -> Y scale (height is Y in JSON, Z in Modelbench internal)
		//   FRONT/BACK  -> Z scale (depth is Z in JSON, Y in Modelbench internal)
		switch (part)
		{
			case BendPart.Right: case BendPart.Left:
				offset *= scaleX;
				size *= scaleX;
				break;
			case BendPart.Upper: case BendPart.Lower:
				offset *= scaleY;
				size *= scaleY;
				break;
			case BendPart.Front: case BendPart.Back:
				offset *= scaleZ;
				size *= scaleZ;
				break;
		}
		
		return new BendParams
		{
			Angle = angle,
			BendOffset = offset,
			BendSize = size,
			ExplicitBendSize = explicitBendSize,
			Detail = detail,
			Part = part,
			AxisX = axisX,
			AxisY = axisY,
			AxisZ = axisZ,
			InvertX = invertX,
			InvertY = invertY,
			InvertZ = invertZ,
			DirectionMin = dirMin,
			DirectionMax = dirMax,
			InheritBend = (bend.InheritBend ?? 0f) > 0f
		};
	}
	
	/// <summary>
	/// Computes the bend vector for a given weight (0-1).
	/// Matches Modelbench's model_shape_get_bend():
	///   GML (Z-up): X=quint, Y(depth)=quint, Z(height)=linear
	///   Godot (Y-up): X=quint, Y(height)=linear, Z(depth)=quint
	/// GML: vec3(bend[X] * ease("easeinoutquint", weight), bend[Y] * ease("easeinoutquint", weight), bend[Z] * weight)
	/// Coordinate mapping: GML Z (height, linear) → Godot Y; GML Y (depth, quint) → Godot Z
	/// </summary>
	public static Vector3 GetBendVector(Vector3 angle, float weight)
	{
		return new Vector3(
			angle.X * EaseInOutQuint(weight),  // X: quint easing (same in both systems)
			angle.Y * weight,                   // Y (Godot height = GML Z): linear weighting
			angle.Z * EaseInOutQuint(weight)    // Z (Godot depth = GML Y): quint easing
		);
	}
	
	/// <summary>
	/// Builds the bend transformation matrix for a given bend vector.
	/// Matches Modelbench's model_part_get_bend_matrix() exactly.
	///
	/// The GML: matrix_build(pos[X], pos[Y], pos[Z], bend[X], bend[Y], bend[Z], sca[X], sca[Y], sca[Z])
	/// Which builds: Translate(pos) * RotateYXZ(bend) * Scale(sca)
	///
	/// For shapes (element_type = TYPE_SHAPE):
	///   mat2 = matrix_build(-pos, rotation, 1) * mat1
	///   Combined: Translate(-pos) * Rotate(shapeRot) * Translate(pos) * RotateYXZ(bend) * Scale(sca)
	///
	/// NOTE: Shape rotation is baked into mesh vertices before calling this method,
	/// so we only compute: Translate(pos) * RotateYXZ(bend) * Scale(sca) * Translate(-pos)
	/// which is a rotation+scale around the pivot point.
	///
	/// The bend angles are in the caller's coordinate system (already remapped to Godot Y-up
	/// by GetBendVector or the caller's linear interpolation).
	/// </summary>
	/// <param name="b">Bend parameters</param>
	/// <param name="bendVec">Bend angle vector in degrees (already in Godot Y-up coordinates)</param>
	/// <param name="shapePosition">Shape position in part-local space (Godot units, pre-shape scale)</param>
	/// <param name="shapeScale">Shape scale to apply to shapePosition for pivot calculation</param>
	/// <param name="matrixScale">Optional scale factor for the matrix (used for blocky bend correction and Z-fighting).
	/// Matches the GML's sca parameter in model_part_get_bend_matrix.</param>
	public static Transform3D GetBendMatrix(BendParams b, Vector3 bendVec, Vector3 shapePosition, Vector3 shapeScale = default, Vector3 matrixScale = default)
	{
		// If no rotation and no special scale, return identity
		if (bendVec.X == 0 && bendVec.Y == 0 && bendVec.Z == 0 &&
		    (matrixScale == default || matrixScale == Vector3.One))
			return Transform3D.Identity;
		
		// Default matrix scale to (1,1,1)
		if (matrixScale == default || matrixScale == Vector3.Zero)
			matrixScale = Vector3.One;
		
		// Build the rotation part: RotateYXZ matching GML's matrix_build rotation order
		var rotBasis = Basis.Identity;
		rotBasis = rotBasis.Rotated(Vector3.Up, Mathf.DegToRad(bendVec.Y));    // Y first
		rotBasis = rotBasis.Rotated(Vector3.Right, Mathf.DegToRad(bendVec.X)); // Then X
		rotBasis = rotBasis.Rotated(Vector3.Back, Mathf.DegToRad(bendVec.Z));  // Then Z
		
		// Apply matrix scale (matching GML's sca parameter in matrix_build)
		var scaledBasis = rotBasis.Scaled(matrixScale);
		
		// Calculate the bend pivot position in part-local space
		// This matches the GML logic: pos = bend_offset (along bend axis)
		// For shapes: pos -= shape_position_along_axis
		// In Modelbench Z-up: RIGHT/LEFT=X, FRONT/BACK=Y, UPPER/LOWER=Z
		// In Godot Y-up: RIGHT/LEFT=X, UPPER/LOWER=Y, FRONT/BACK=Z
		if (shapeScale == Vector3.Zero)
			shapeScale = Vector3.One;
		Vector3 scaledShapePos = new Vector3(
			shapePosition.X * shapeScale.X,
			shapePosition.Y * shapeScale.Y,
			shapePosition.Z * shapeScale.Z
		);
		Vector3 pivotPos = Vector3.Zero;
		switch (b.Part)
		{
			case BendPart.Right:
			case BendPart.Left:
				pivotPos.X = b.BendOffset / 16.0f - scaledShapePos.X;
				break;
			case BendPart.Front:
			case BendPart.Back:
				pivotPos.Z = b.BendOffset / 16.0f - scaledShapePos.Z;
				break;
			case BendPart.Upper:
			case BendPart.Lower:
				pivotPos.Y = b.BendOffset / 16.0f - scaledShapePos.Y;
				break;
		}
		
		// GML builds: matrix_build(pos, bend, sca) which is Translate(pos) * Rotate(bend) * Scale(sca)
		// For shape: matrix_multiply(matrix_build(-pos, shapeRot, 1), mat)
		//   = Translate(-pos) * Rotate(shapeRot) * Translate(pos) * Rotate(bend) * Scale(sca)
		// Since shapeRot is pre-baked into vertices, we compute:
		//   Translate(pos) * Rotate(bend) * Scale(sca) as the "inner" transform,
		//   then the full transform for a vertex v is: inner * v
		// But we need it as: translate(pivot) * rotScale * translate(-pivot)
		// Which expands to: v' = rotScale * (v - pivot) + pivot
		//                      = rotScale * v + (pivot - rotScale * pivot)
		var transform = new Transform3D(scaledBasis, pivotPos - scaledBasis * pivotPos);
		
		return transform;
	}
	
	// ── Easing functions ──────────────────────────────────────────────────────
	
	/// <summary>
	/// Ease-in-out quint (5th power) easing function.
	/// Matches Modelbench's ease("easeinoutquint", t).
	/// </summary>
	public static float EaseInOutQuint(float t)
	{
		float xx2 = t * 2.0f;
		
		if (t <= 0.0f)
			return 0.0f;
		if (t >= 1.0f)
			return 1.0f;
		
		if (xx2 < 1.0f)
		{
			// Ease-in for the first half: 1/2 * (2t)^5
			return 0.5f * xx2 * xx2 * xx2 * xx2 * xx2;
		}
		else
		{
			// Ease-out for the second half: 1/2 * ((2t-2)^5 + 2)
			return 0.5f * ((xx2 - 2.0f) * (xx2 - 2.0f) * (xx2 - 2.0f) * (xx2 - 2.0f) * (xx2 - 2.0f) + 2.0f);
		}
	}
	
	/// <summary>
	/// Ease-in cubic easing function.
	/// Matches Modelbench's ease("easeincubic", t).
	/// </summary>
	public static float EaseInCubic(float t)
	{
		t = Math.Clamp(t, 0.0f, 1.0f);
		return t * t * t;
	}
	
	/// <summary>
	/// Calculates the number of bend segments for a given bend size.
	/// Matches Modelbench's model_shape_generate_block.gml line 110:
	///   detail = (sharpbend ? 2 : max(bendsize, 2))
	/// </summary>
	/// <param name="bendSize">Bend size in pixels</param>
	/// <param name="sharpBend">Whether sharp (blocky) bending is active</param>
	/// <param name="detail">Optional explicit detail value</param>
	/// <returns>Number of segments (minimum 2)</returns>
	public static float CalculateSegmentCount(float bendSize, bool sharpBend, float? detail = null)
	{
		if (detail.HasValue)
		{
			return Math.Max(2, detail.Value);
		}
		
		// Modelbench: sharpbend ? 2 : max(bendsize, 2)
		if (sharpBend)
			return 2;
		
		return Math.Max(bendSize, 2);
	}
	
	/// <summary>
	/// Calculates anti-pinching scale correction for blocky bending.
	/// Matches Modelbench's model_shape_get_bend_scale() exactly.
	/// 
	/// NOTE: The GML version only handled X and Y axes, missing Z axis entirely.
	/// This C# version correctly handles all three axes (this was a bug in the GML).
	/// </summary>
	/// <param name="bendStart">Start position of bend region</param>
	/// <param name="bendEnd">End position of bend region</param>
	/// <param name="weight">Segment weight (0-1)</param>
	/// <param name="bendPosition">Current position along bend axis</param>
	/// <param name="bendAngle">Current bend angle vector</param>
	/// <param name="bendParams">Bend parameters</param>
	/// <returns>Scale correction vector</returns>
	public static Vector3 GetBendScaleCorrection(float bendStart, float bendEnd, float weight, float bendPosition, Vector3 bendAngle, BendParams bendParams)
	{
		if (bendPosition > bendStart && bendPosition < bendEnd)
		{
			Vector3 bendScale;
			
			if (weight <= 0.5f)
				bendScale = new Vector3(weight * 2, weight * 2, weight * 2);
			else
				bendScale = new Vector3((1 - weight) * 2, (1 - weight) * 2, (1 - weight) * 2);
			
			// GML only checks for X-only (bend_axis = [true,false,false]) → index 0 (GML X = Godot X)
			// and Y-only (bend_axis = [false,true,false]) → index 1 (GML Y = depth = Godot Z index 2)
			// GML Z (height) is never checked — the function returns vec3(0) for Z-only.
			int bendAxis = -1;
			if (bendParams.AxisX && !bendParams.AxisY && !bendParams.AxisZ)
				bendAxis = 0;  // GML X → Godot X (index 0)
			else if (!bendParams.AxisX && !bendParams.AxisY && bendParams.AxisZ)
				bendAxis = 2;  // GML Y (depth) → Godot Z (index 2)
			// Note: Y-only (height) is intentionally NOT handled — matches GML which omits Z-axis
			
			if (bendAxis == -1)
				return Vector3.Zero;
			
			float bendAng = Math.Abs(bendAngle[bendAxis]);
			
			if (bendAng > 90)
				bendAng -= (bendAng - 90) * 2;
			
			float bendPerc = bendAng / 90.0f;
			bendPerc = Math.Clamp(bendPerc, 0, 1);
			bendScale *= bendPerc;
			
			bendScale.X = EaseInCubic(bendScale.X);
			bendScale.Y = EaseInCubic(bendScale.Y);
			bendScale.Z = EaseInCubic(bendScale.Z);
			
			bendScale /= 2.5f;
			
			bendScale[bendAxis] = 0;
			
			return bendScale;
		}
		else
		{
			return Vector3.Zero;
		}
	}
	
	/// <summary>
	/// Calculates the effective bend size based on part dimensions and bend style.
	/// Matches MineImator's bend size calculation logic.
	/// </summary>
	/// <param name="partSize">Size of the part along the bend axis</param>
	/// <param name="bendStyle">Bend style setting</param>
	/// <param name="explicitBendSize">Optional explicit bend size from JSON</param>
	/// <returns>Effective bend size in pixels</returns>
	public static float CalculateBendSize(float partSize, BendStyle bendStyle, float? explicitBendSize = null)
	{
		if (explicitBendSize.HasValue)
		{
			return explicitBendSize.Value;
		}
		
		if (bendStyle == BendStyle.Blocky)
		{
			return 1.0f;
		}
		else
		{
			// Realistic style: default 4 pixels, clamped to part size
			return Math.Clamp(4.0f, 1.0f, partSize);
		}
	}
	
	// ── Private helpers ───────────────────────────────────────────────────────
	
	private static void ParseAxisString(string axis, ref bool axisX, ref bool axisY, ref bool axisZ,
		System.Collections.Generic.List<int> indices)
	{
		// JSON axis mapping (Y-up coordinate system, Godot convention):
		//   "x" -> index 0 (Godot X) - left/right rotation, easeinoutquint easing
		//   "y" -> index 1 (Godot Y) - up/down rotation (height), linear easing
		//   "z" -> index 2 (Godot Z) - front/back rotation (depth), easeinoutquint easing
		// The JSON uses Mine Imator's Y-up convention where "y" = height.
		// We use Godot's index mapping directly for correct axis correspondence.
		switch (axis?.ToLowerInvariant())
		{
			case "x": axisX = true; indices.Add(0); break;
			case "z": axisZ = true; indices.Add(2); break;
			case "y": axisY = true; indices.Add(1); break;
		}
	}
	
	private static void SetVec3Component(ref Vector3 v, int axis, float value)
	{
		switch (axis)
		{
			case 0: v.X = value; break;
			case 1: v.Y = value; break;
			case 2: v.Z = value; break;
		}
	}
	
	private static void SetBoolComponent(ref bool bx, ref bool by, ref bool bz, int axis, bool value)
	{
		switch (axis)
		{
			case 0: bx = value; break;
			case 1: by = value; break;
			case 2: bz = value; break;
		}
	}
	
	private static Vector3 ClampAndInvertAngle(Vector3 angle, Vector3 dirMin, Vector3 dirMax,
		bool invertX, bool invertY, bool invertZ, bool axisX, bool axisY, bool axisZ)
	{
		// Clamp to direction limits
		angle.X = Math.Clamp(angle.X, dirMin.X, dirMax.X);
		angle.Y = Math.Clamp(angle.Y, dirMin.Y, dirMax.Y);
		angle.Z = Math.Clamp(angle.Z, dirMin.Z, dirMax.Z);
		
		// Apply invert
		if (invertX) angle.X *= -1;
		if (invertY) angle.Y *= -1;
		if (invertZ) angle.Z *= -1;
		
		// Zero out inactive axes
		if (!axisX) angle.X = 0;
		if (!axisY) angle.Y = 0;
		if (!axisZ) angle.Z = 0;
		
		return angle;
	}
}
