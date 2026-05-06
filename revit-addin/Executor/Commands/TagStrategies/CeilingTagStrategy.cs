// ============================================================================
// A49AIRevitAssistant/Executor/Commands/TagStrategies/CeilingTagStrategy.cs
// ============================================================================
// Tags ceilings in Ceiling Plan views only.
//
// CEILING PLAN:
//   - Tag placed at the ceiling element's bounding-box center (centroid)
//   - No leader
//   - Tag orientation: Horizontal
//
// Note: Ceiling tags are NOT supported in Floor Plan, Elevations, or Sections
// per firm standard.
// ============================================================================

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace A49AIRevitAssistant.Executor.Commands.TagStrategies
{
    public class CeilingTagStrategy : ITagStrategy
    {
        public string CategoryKey => "ceiling";
        public BuiltInCategory TargetCategory => BuiltInCategory.OST_Ceilings;
        public BuiltInCategory TagCategory => BuiltInCategory.OST_CeilingTags;

        public bool SupportsViewType(ViewType viewType)
        {
            // Ceiling Plan only per firm standard
            return viewType == ViewType.CeilingPlan;
        }

        public TagResult TagElementsInView(
            Document doc,
            View view,
            FamilySymbol tagSymbol,
            bool skipTagged)
        {
            var result = new TagResult();

            var ceilingCollector = new FilteredElementCollector(doc, view.Id)
                .OfCategory(TargetCategory)
                .WhereElementIsNotElementType();

            // Pre-collect already-tagged ceiling IDs
            var alreadyTaggedIds = new HashSet<long>();
            if (skipTagged)
            {
                var tagCollector = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(IndependentTag));

                foreach (IndependentTag existingTag in tagCollector)
                {
                    try
                    {
                        foreach (var taggedId in existingTag.GetTaggedLocalElementIds())
                        {
                            Element el = doc.GetElement(taggedId);
                            if (el != null && el.Category != null &&
                                el.Category.BuiltInCategory == TargetCategory)
                            {
                                alreadyTaggedIds.Add(taggedId.Value);
                            }
                        }
                    }
                    catch { }
                }
            }

            foreach (Element ceiling in ceilingCollector)
            {
                try
                {
                    if (skipTagged && alreadyTaggedIds.Contains(ceiling.Id.Value))
                    {
                        result.Skipped++;
                        continue;
                    }

                    // Get ceiling centroid from bounding box in this view
                    BoundingBoxXYZ bbox = ceiling.get_BoundingBox(view);
                    if (bbox == null)
                    {
                        result.Errors.Add($"No bounding box for ceiling {ceiling.Id.Value} in '{view.Name}'.");
                        continue;
                    }

                    XYZ centroid = (bbox.Min + bbox.Max) * 0.5;

                    var ceilingRef = new Reference(ceiling);
                    var newTag = IndependentTag.Create(
                        doc,
                        tagSymbol.Id,
                        view.Id,
                        ceilingRef,
                        false,                      // no leader
                        TagOrientation.Horizontal,
                        centroid
                    );

                    if (newTag != null) result.Tagged++;
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Ceiling {ceiling.Id.Value}: {ex.Message}");
                }
            }

            // ====================================================================
            // LINKED PASS — ceilings in linked Revit models. Tag at the
            // bbox centroid (same as host pass) but in host coords.
            // ====================================================================
            foreach (var link in LinkedTagHelpers.EnumerateLinks(doc, view))
            {
                HashSet<long> linkedAlreadyTagged = skipTagged
                    ? LinkedTagHelpers.CollectAlreadyTaggedLinkedIds(doc, view, link.Instance.Id, TargetCategory)
                    : new HashSet<long>();

                FilteredElementCollector linkedCeilings;
                try
                {
                    linkedCeilings = new FilteredElementCollector(link.Document)
                        .OfCategory(TargetCategory)
                        .WhereElementIsNotElementType();
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Linked doc '{link.Document.Title}': {ex.Message}");
                    continue;
                }

                foreach (Element ceiling in linkedCeilings)
                {
                    try
                    {
                        if (skipTagged && linkedAlreadyTagged.Contains(ceiling.Id.Value))
                        {
                            result.Skipped++;
                            continue;
                        }

                        // The (view) overload is unsafe across docs — read the
                        // intrinsic bbox in link coords, take centroid, then
                        // transform to host.
                        BoundingBoxXYZ bbox = ceiling.get_BoundingBox(null);
                        if (bbox == null)
                        {
                            result.Errors.Add($"No bounding box for linked ceiling {ceiling.Id.Value}.");
                            continue;
                        }
                        XYZ centroidLink = (bbox.Min + bbox.Max) * 0.5;
                        XYZ centroidHost = link.Transform.OfPoint(centroidLink);

                        // Skip ceilings whose centroid falls outside this view's
                        // crop region (best-effort visibility check — ceiling
                        // plans usually don't crop, but scope-boxed views do).
                        if (!TagHelpers.IsElementInCropRegion(view, centroidHost))
                        {
                            result.Skipped++;
                            continue;
                        }

                        Reference linkedRef = LinkedTagHelpers.BuildLinkedReference(ceiling, link.Instance);
                        if (linkedRef == null)
                        {
                            result.Errors.Add($"Could not build link reference for ceiling {ceiling.Id.Value}.");
                            continue;
                        }

                        var newTag = IndependentTag.Create(
                            doc, tagSymbol.Id, view.Id,
                            linkedRef, false, TagOrientation.Horizontal, centroidHost);

                        if (newTag != null) result.Tagged++;
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"Linked ceiling {ceiling.Id.Value}: {ex.Message}");
                    }
                }
            }

            return result;
        }
    }
}
