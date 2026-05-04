// ============================================================================
// A49AIRevitAssistant/Executor/Commands/TagStrategies/TagContext.cs
// ============================================================================
// Shared data classes and helpers used by the Automate Tagging system:
//   - TagRequest: input payload from the wizard
//   - TagResult:  per-view result returned from a strategy
//   - TagHelpers: static helpers for section / elevation / crop region checks
// ============================================================================

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace A49AIRevitAssistant.Executor.Commands.TagStrategies
{
    /// <summary>
    /// Single wizard submission — what to tag, where to tag it, how.
    /// </summary>
    public class TagRequest
    {
        public string TagTypeCategory { get; set; }  // "door", "window", "wall", "room", "ceiling"
        public string TagFamily { get; set; }
        public string TagTypeName { get; set; }
        public List<ElementId> ViewIds { get; set; }
        public bool SkipTagged { get; set; } = true;
    }

    /// <summary>
    /// Per-view tagging result aggregated by the orchestrator.
    /// </summary>
    public class TagResult
    {
        public int Tagged { get; set; } = 0;
        public int Skipped { get; set; } = 0;
        public List<string> Errors { get; set; } = new List<string>();
    }

    /// <summary>
    /// Shared helper methods used by multiple tag strategies.
    /// </summary>
    public static class TagHelpers
    {
        // ====================================================================
        // SECTION VIEW VISIBILITY
        // ====================================================================

        /// <summary>
        /// For SECTION views: determines whether a door or window should be tagged.
        ///
        /// TWO ZONES are checked:
        ///
        ///   ZONE 1 — CUT PLANE (±300mm tolerance):
        ///     The element is directly intersected by the section cut.
        ///     300mm covers a typical wall thickness so elements hosted on
        ///     the cut wall are included on both sides of the wall face.
        ///     → ALWAYS tag these.
        ///
        ///   ZONE 2 — FAR-CLIP ELEVATION ZONE (beyond cut plane, within far clip):
        ///     The element is visible on the facing wall in the background.
        ///     This happens when the Far Clip Offset is extended far enough to
        ///     reveal the elevation of the back wall.
        ///     → Tag ONLY if the element's host wall faces the section
        ///       (i.e. the wall is parallel to the section cut plane).
        ///
        ///   SKIP — Beyond the far clip, or on side walls not cut by the section.
        /// </summary>
        public static bool IsElementVisibleInSection(View sectionView, FamilyInstance element, XYZ elementPoint)
        {
            try
            {
                if (sectionView.ViewType != ViewType.Section) return true;

                BoundingBoxXYZ cropBox = sectionView.CropBox;
                if (cropBox == null) return true;

                // Transform element world point → view local coordinates.
                // In section local coords:
                //   X = left/right  |  Y = up/down  |  Z = depth (cut → far clip)
                Transform inv = cropBox.Transform.Inverse;
                XYZ localPoint = inv.OfPoint(elementPoint);

                double cutPlaneZ = cropBox.Min.Z;        // near clip = cut plane
                double farClipZ = cropBox.Max.Z;        // far clip
                double cutTolerance = 300.0 / 304.8;        // 300mm in feet

                // ZONE 1: Directly cut by the section plane → always tag
                if (Math.Abs(localPoint.Z - cutPlaneZ) <= cutTolerance)
                    return true;

                // ZONE 2: In the far-clip elevation zone → tag only if on facing wall
                double beyondCut = cutPlaneZ + cutTolerance;
                if (localPoint.Z > beyondCut && localPoint.Z <= farClipZ)
                    return IsElementOnFacingWallOfSection(sectionView, element);

                // Beyond far clip or behind section → skip
                return false;
            }
            catch
            {
                return true;
            }
        }

        // ====================================================================
        // ELEVATION VIEW — FACING WALL CHECK
        // ====================================================================

        /// <summary>
        /// For ELEVATION views (including interior elevations): checks whether
        /// the element's host wall is the FACING wall of the elevation.
        /// Elements on walls behind or perpendicular to the view are excluded.
        ///
        /// Logic: The elevation's ViewDirection points INTO the view.
        /// A wall "faces" the elevation if its normal is roughly parallel
        /// to the ViewDirection (dot product > 0.7).
        /// </summary>
        public static bool IsElementOnFacingWall(View elevationView, FamilyInstance element)
        {
            try
            {
                if (elevationView.ViewType != ViewType.Elevation) return true;

                Wall hostWall = element.Host as Wall;
                if (hostWall == null) return true;

                LocationCurve locCurve = hostWall.Location as LocationCurve;
                if (locCurve == null) return true;

                Line wallLine = locCurve.Curve as Line;
                if (wallLine == null) return true; // Curved wall — include

                XYZ wallDir = wallLine.Direction;
                XYZ wallNormal = new XYZ(-wallDir.Y, wallDir.X, 0).Normalize();
                XYZ viewDir = elevationView.ViewDirection;

                double dot = Math.Abs(wallNormal.DotProduct(viewDir));
                return dot > 0.7;
            }
            catch
            {
                return true;
            }
        }

        // ====================================================================
        // CROP REGION HELPERS (Scope Box boundary enforcement)
        // ====================================================================

        /// <summary>
        /// Checks whether an element's location point falls within the view's
        /// active crop box region (XY bounds only).
        ///
        /// Used to skip elements that are technically collected by
        /// FilteredElementCollector but sit outside the scope box boundary.
        /// Returns true if the view has no active crop box.
        /// </summary>
        public static bool IsElementInCropRegion(View view, XYZ worldPoint)
        {
            try
            {
                if (!view.CropBoxActive) return true;

                BoundingBoxXYZ cropBox = view.CropBox;
                if (cropBox == null) return true;

                Transform inv = cropBox.Transform.Inverse;
                XYZ localPoint = inv.OfPoint(worldPoint);

                // XY bounds only (Z = depth, irrelevant for plan views)
                return localPoint.X >= cropBox.Min.X && localPoint.X <= cropBox.Max.X
                    && localPoint.Y >= cropBox.Min.Y && localPoint.Y <= cropBox.Max.Y;
            }
            catch
            {
                return true;
            }
        }

        /// <summary>
        /// Ensures a tag head point stays within the view's active crop box.
        /// If the calculated tag point falls outside the crop region it is
        /// clamped inward by the given margin (in feet).
        ///
        /// This prevents tag text from rendering outside the scope box boundary
        /// when an element sits near the crop edge and the offset pushes the
        /// tag head beyond it.
        ///
        /// Returns the original point unchanged if no active crop box exists,
        /// or if no clamping was needed.
        /// </summary>
        public static XYZ ClampTagPointToCropRegion(View view, XYZ tagWorldPoint, double marginFeet = 0.0)
        {
            try
            {
                if (!view.CropBoxActive) return tagWorldPoint;

                BoundingBoxXYZ cropBox = view.CropBox;
                if (cropBox == null) return tagWorldPoint;

                Transform fwd = cropBox.Transform;
                Transform inv = fwd.Inverse;

                XYZ localPoint = inv.OfPoint(tagWorldPoint);

                double clampedX = Math.Max(cropBox.Min.X + marginFeet,
                                  Math.Min(cropBox.Max.X - marginFeet, localPoint.X));
                double clampedY = Math.Max(cropBox.Min.Y + marginFeet,
                                  Math.Min(cropBox.Max.Y - marginFeet, localPoint.Y));

                // Return original if no clamping was needed
                if (Math.Abs(clampedX - localPoint.X) < 0.001 &&
                    Math.Abs(clampedY - localPoint.Y) < 0.001)
                    return tagWorldPoint;

                XYZ clampedLocal = new XYZ(clampedX, clampedY, localPoint.Z);
                return fwd.OfPoint(clampedLocal);
            }
            catch
            {
                return tagWorldPoint;
            }
        }

        // ====================================================================
        // PRIVATE HELPERS
        // ====================================================================

        /// <summary>
        /// For SECTION Zone 2: checks whether the element's host wall is the
        /// FACING WALL of the section (the back wall being looked at).
        /// Same dot-product logic as IsElementOnFacingWall for elevations.
        /// </summary>
        private static bool IsElementOnFacingWallOfSection(View sectionView, FamilyInstance element)
        {
            try
            {
                Wall hostWall = element.Host as Wall;
                if (hostWall == null) return true;

                LocationCurve locCurve = hostWall.Location as LocationCurve;
                if (locCurve == null) return true;

                Line wallLine = locCurve.Curve as Line;
                if (wallLine == null) return true;

                XYZ wallDir = wallLine.Direction;
                XYZ wallNormal = new XYZ(-wallDir.Y, wallDir.X, 0).Normalize();
                XYZ viewDir = sectionView.ViewDirection;

                double dot = Math.Abs(wallNormal.DotProduct(viewDir));
                return dot > 0.7;
            }
            catch
            {
                return true;
            }
        }
    }
}
