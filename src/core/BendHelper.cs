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
	public static BendParams? ParseBend(MiBend bend, float[] partScale)
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
		
		// Parse offset and size (in pixels)
		float offset = bend.Offset ?? 0.0f;
		float size = bend.Size ?? 4.0f; // Modelbench default is 4 pixels
		
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
			Part = part,
			AxisX = axisX,
			AxisY = axisY,
			AxisZ = axisZ,
			InvertX = invertX,
			InvertY = invertY,
			InvertZ = invertZ,
			DirectionMin = dirMin,
			DirectionMax = dirMax
		};
	}
	
	/// <summary>
	/// Computes the bend vector for a given weight (0-1).
	/// Matches Modelbench's model_shape_get_bend():
	///   X/Y use easeinoutquint easing, Z uses linear.
	/// </summary>
	public static Vector3 GetBendVector(Vector3 angle, float weight)
	{
		return new Vector3(
			angle.X * EaseInOutQuint(weight),
			angle.Y * EaseInOutQuint(weight),
			angle.Z * weight
		);
	}
	
	/// <summary>
	/// Builds the bend transformation matrix for a given bend vector.
	/// Matches Modelbench's model_part_get_bend_matrix() exactly.
	///
	/// The GML applies TWO matrix multiplications:
	///   mat1 = matrix_build(pos, bend, scale)   → v1 = R_bend * v + pos
	///   mat2 = matrix_build(-pos, rotation, 1)  → v2 = R_rot * v1 - pos
	///   Combined: v2 = R_rot * R_bend * v + R_rot * pos - pos
	///
	/// For zero shape rotation (R_rot = I):
	///   v2 = R_bend * v + pos - pos = R_bend * v
	///
	/// So the final transform is just a ROTATION AROUND THE ORIGIN (no translation).
	/// The pos/T values cancel out completely.
	/// 
	/// IMPORTANT: The bend angles are stored in Modelbench's internal Z-up coordinate system:
	///   - bend.X = left/right rotation (around Modelbench X axis)
	///   - bend.Y = front/back rotation (around Modelbench Y axis in Z-up, becomes Godot Z)
	///   - bend.Z = up/down rotation (around Modelbench Z axis in Z-up, becomes Godot Y)
	/// We need to remap to Godot's Y-up axes for proper rotation.
	/// </summary>
	/// <param name="b">Bend parameters</param>
	/// <param name="bendVec">Bend angle vector in degrees (Modelbench internal Z-up axes)</param>
	/// <param name="shapePosition">Shape position in part-local space (Godot units) - unused for zero-rotation shapes</param>
	/// <param name="shapeRotation">Shape rotation in radians (Godot Y-up Euler angles) - applied when non-zero</param>
	public static Transform3D GetBendMatrix(BendParams b, Vector3 bendVec, Vector3 shapePosition, Vector3 shapeRotation = default)
	{
		// If no rotation, return identity
		if (bendVec.X == 0 && bendVec.Y == 0 && bendVec.Z == 0)
			return Transform3D.Identity;
		
		// Convert from Modelbench Z-up to Godot Y-up coordinate system:
		// Modelbench X (left/right) -> Godot X (same)
		// Modelbench Y (front/back in Z-up) -> Godot Z (front/back)
		// Modelbench Z (up/down in Z-up) -> Godot Y (up/down)
		// So we swap: bendVec.Y <-> bendVec.Z
		float godotX = bendVec.X;  // Modelbench X -> Godot X
		float godotY = bendVec.Z;  // Modelbench Z (up/down in Z-up) -> Godot Y
		float godotZ = bendVec.Y;  // Modelbench Y (front/back in Z-up) -> Godot Z
		
		// Build rotation from bend angles (in degrees -> radians).
		// Modelbench's matrix_build(pos, rotX, rotY, rotZ, scale) applies rotations in YXZ order.
		// The bendVec contains (Modelbench X rot, Modelbench Y rot, Modelbench Z rot).
		// In Godot Y-up:
		//   - Modelbench X (left/right) -> Godot X axis
		//   - Modelbench Y (front/back in Z-up) -> Godot Z axis
		//   - Modelbench Z (up/down in Z-up) -> Godot Y axis
		// So bendVec = (rotX, rotY, rotZ) becomes Godot euler = (godotY, godotX, godotZ)
		// But the rotations are applied as: Y first, then X, then Z.
		Vector3 godotEuler = new Vector3(godotY, godotX, godotZ);
		
		// Apply rotations in YXZ order to match matrix_build
		var transform = Transform3D.Identity;
		transform = transform.Rotated(Vector3.Up, Mathf.DegToRad(godotEuler.X));   // Y first
		transform = transform.Rotated(Vector3.Right, Mathf.DegToRad(godotEuler.Y)); // Then X
		transform = transform.Rotated(Vector3.Forward, Mathf.DegToRad(godotEuler.Z));  // Then Z
		
		// Calculate the bend pivot position in part-local space
		// This matches the GML logic: pos = bend_offset - shape_position_along_axis
		// In Modelbench Z-up: RIGHT/LEFT=X, FRONT/BACK=Y, UPPER/LOWER=Z
		// In Godot Y-up: RIGHT/LEFT=X, UPPER/LOWER=Y, FRONT/BACK=Z
		Vector3 pivotPos = Vector3.Zero;
		switch (b.Part)
		{
			case BendPart.Right:
			case BendPart.Left:
				// X axis - pivot along X
				pivotPos.X = b.BendOffset / 16.0f - shapePosition.X;
				break;
			case BendPart.Front:
			case BendPart.Back:
				// Z axis in Godot (depth) - pivot along Z
				pivotPos.Z = b.BendOffset / 16.0f - shapePosition.Z;
				break;
			case BendPart.Upper:
			case BendPart.Lower:
				// Y axis in Godot (height) - pivot along Y
				pivotPos.Y = b.BendOffset / 16.0f - shapePosition.Y;
				break;
		}
		
		// Apply the transformation: translate(pivot) * rotate * translate(-pivot)
		var translateBack = Transform3D.Identity;
		translateBack.Origin = -pivotPos;
		var translateForward = Transform3D.Identity;
		translateForward.Origin = pivotPos;
		
		// Final transform: translate(pivot) * rotate * translate(-pivot)
		var result = translateForward * transform * translateBack;
		
		// Apply shape rotation if present (second matrix multiplication from Modelbench)
		// Modelbench: mat = matrix_multiply(matrix_build(-pos, rotation, 1), mat)
		// This applies the bend first, then the rotation.
		// Combined: v2 = R_rot * (R_bend * (v - pivot) + pivot) - pivot
		//            = R_rot*R_bend*v - R_rot*R_bend*pivot + R_rot*pivot - pivot
		if (shapeRotation != Vector3.Zero)
		{
			// Build rotation transform from shape's Euler angles (already in radians, Godot Y-up)
			var shapeRotTransform = Transform3D.Identity;
			shapeRotTransform = shapeRotTransform.Rotated(Vector3.Up, shapeRotation.Y);      // Y first
			shapeRotTransform = shapeRotTransform.Rotated(Vector3.Right, shapeRotation.X);  // Then X
			shapeRotTransform = shapeRotTransform.Rotated(Vector3.Forward, shapeRotation.Z); // Then Z
			
			// Build the matrix_build(pos, rotation, 1) transform
			var translateNegPivot = Transform3D.Identity;
			translateNegPivot.Origin = pivotPos;
			
			// The correct formula is: matrix_build(-pos, rotation, 1) * matrix_build(pos, bend, 1)
			// Which translates to: translate(-pivot) * R(shape) * translate(pivot) * R(bend) * translate(-pivot)
			result = translateNegPivot * shapeRotTransform * translateForward * transform * translateBack;
		}
		
		return result;
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
	
	// ── Private helpers ───────────────────────────────────────────────────────
	
	private static void ParseAxisString(string axis, ref bool axisX, ref bool axisY, ref bool axisZ,
		System.Collections.Generic.List<int> indices)
	{
		// Modelbench internal axis mapping (Z-up coordinate system):
		//   "x" -> internal X (index 0) - left/right rotation, easeinoutquint easing
		//   "z" -> internal Y (index 1) - front/back rotation, easeinoutquint easing
		//   "y" -> internal Z (index 2) - up/down rotation, linear easing
		// The JSON uses Mine Imator's Y-up convention where "y" = height.
		// We keep Modelbench's internal mapping to preserve the correct easing behavior.
		switch (axis?.ToLowerInvariant())
		{
			case "x": axisX = true; indices.Add(0); break;
			case "z": axisY = true; indices.Add(1); break;
			case "y": axisZ = true; indices.Add(2); break;
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
