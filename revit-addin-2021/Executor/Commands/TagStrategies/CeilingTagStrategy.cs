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
//
// Revit 2021 adaptations vs canonical (revit-addin/):
//   - IndependentTag.GetTaggedLocalElementIds() → TaggedLocalElementId (single)
//   - Category.BuiltInCategory → (BuiltInCategory)Category.Id.IntegerValue
//   - ElementId.Value → ElementId.IntegerValue
//   - Linked pass is dead code (LinkedTagHelpers stubbed) — kept for parity.
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
                        ElementId taggedId = existingTag.TaggedLocalElementId;
                        if (taggedId != null && taggedId != ElementId.InvalidElementId)
                        {
                            Element el = doc.GetElement(taggedId);
                            if (el != null && el.Category != null &&
                                (BuiltInCategory)el.Category.Id.IntegerValue == TargetCategory)
                            {
                                alreadyTaggedIds.Add(taggedId.IntegerValue);
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
                    if (skipTagged && alreadyTaggedIds.Contains(ceiling.Id.IntegerValue))
                    {
                        result.Skipped++;
                        continue;
                    }

                    // Get ceiling centroid from bounding box in this view
                    BoundingBoxXYZ bbox = ceiling.get_BoundingBox(view);
                    if (bbox == null)
                    {
                        result.Errors.Add($"No bounding box for ceiling {ceiling.Id.IntegerValue} in '{view.Name}'.");
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
                    result.Errors.Add($"Ceiling {ceiling.Id.IntegerValue}: {ex.Message}");
                }
            }

            // ====================================================================
            // LINKED PASS — REVIT 2021: stubbed via LinkedTagHelpers (yields nothing).
            // Loop body never executes. Kept for structural parity with revit-addin/.
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
                        if (skipTagged && linkedAlreadyTagged.Contains(ceiling.Id.IntegerValue))
                        {
                            result.Skipped++;
                            continue;
                        }

                        BoundingBoxXYZ bbox = ceiling.get_BoundingBox(null);
                        if (bbox == null)
                        {
                            result.Errors.Add($"No bounding box for linked ceiling {ceiling.Id.IntegerValue}.");
                            continue;
                        }
                        XYZ centroidLink = (bbox.Min + bbox.Max) * 0.5;
                        XYZ centroidHost = link.Transform.OfPoint(centroidLink);

                        if (!TagHelpers.IsElementInCropRegion(view, centroidHost))
                        {
                            result.Skipped++;
                            continue;
                        }

                        Reference linkedRef = LinkedTagHelpers.BuildLinkedReference(ceiling, link.Instance);
                        if (linkedRef == null)
                        {
                            // Revit 2021: BuildLinkedReference always returns null. Silent skip.
                            continue;
                        }

                        var newTag = IndependentTag.Create(
                            doc, tagSymbol.Id, view.Id,
                            linkedRef, false, TagOrientation.Horizontal, centroidHost);

                        if (newTag != null) result.Tagged++;
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"Linked ceiling {ceiling.Id.IntegerValue}: {ex.Message}");
                    }
                }
            }

            return result;
        }
    }
}
