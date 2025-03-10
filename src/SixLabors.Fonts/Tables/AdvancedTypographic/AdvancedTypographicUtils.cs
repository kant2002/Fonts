// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using SixLabors.Fonts.Tables.AdvancedTypographic.GPos;
using SixLabors.Fonts.Unicode;

namespace SixLabors.Fonts.Tables.AdvancedTypographic
{
    internal static class AdvancedTypographicUtils
    {
        /// <summary>
        /// The maximum length of a context. Taken from HarfBuzz - hb-ot-layout-common.hh
        /// </summary>
        public const int MaxContextLength = 64;

        public static bool ApplyLookupList(
            FontMetrics fontMetrics,
            GSubTable table,
            Tag feature,
            LookupFlags lookupFlags,
            SequenceLookupRecord[] records,
            GlyphSubstitutionCollection collection,
            ushort index,
            int count)
        {
            bool hasChanged = false;
            SkippingGlyphIterator iterator = new(fontMetrics, collection, index, lookupFlags);
            int currentCount = collection.Count;

            foreach (SequenceLookupRecord lookupRecord in records)
            {
                ushort sequenceIndex = lookupRecord.SequenceIndex;
                ushort lookupIndex = lookupRecord.LookupListIndex;
                iterator.Index = index;
                iterator.Increment(sequenceIndex);
                GSub.LookupTable lookup = table.LookupList.LookupTables[lookupIndex];
                hasChanged |= lookup.TrySubstitution(fontMetrics, table, collection, feature, iterator.Index, count - (iterator.Index - index));

                // Account for substitutions changing the length of the collection.
                if (collection.Count != currentCount)
                {
                    count -= currentCount - collection.Count;
                    currentCount = collection.Count;
                }
            }

            return hasChanged;
        }

        public static bool ApplyLookupList(
            FontMetrics fontMetrics,
            GPosTable table,
            Tag feature,
            LookupFlags lookupFlags,
            SequenceLookupRecord[] records,
            GlyphPositioningCollection collection,
            ushort index,
            int count)
        {
            bool hasChanged = false;
            SkippingGlyphIterator iterator = new(fontMetrics, collection, index, lookupFlags);
            foreach (SequenceLookupRecord lookupRecord in records)
            {
                ushort sequenceIndex = lookupRecord.SequenceIndex;
                ushort lookupIndex = lookupRecord.LookupListIndex;
                iterator.Index = index;
                iterator.Increment(sequenceIndex);
                LookupTable lookup = table.LookupList.LookupTables[lookupIndex];
                hasChanged |= lookup.TryUpdatePosition(fontMetrics, table, collection, feature, iterator.Index, count - (iterator.Index - index));
            }

            return hasChanged;
        }

        public static bool MatchInputSequence(SkippingGlyphIterator iterator, Tag feature, ushort increment, ushort[] sequence, Span<int> matches)
            => Match(
                increment,
                sequence,
                iterator,
                (component, data) =>
                {
                    if (!ContainsFeatureTag(data.Features, feature))
                    {
                        return false;
                    }

                    return component == data.GlyphId;
                },
                matches);

        private static bool ContainsFeatureTag(List<TagEntry> featureList, Tag feature)
        {
            foreach (TagEntry tagEntry in featureList)
            {
                if (tagEntry.Tag == feature && tagEntry.Enabled)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool MatchSequence(SkippingGlyphIterator iterator, int increment, ushort[] sequence)
            => Match(
                increment,
                sequence,
                iterator,
                (component, data) => component == data.GlyphId,
                default);

        public static bool MatchClassSequence(
            SkippingGlyphIterator iterator,
            int increment,
            ushort[] sequence,
            ClassDefinitionTable classDefinitionTable)
            => Match(
                increment,
                sequence,
                iterator,
                (component, data) => component == classDefinitionTable.ClassIndexOf(data.GlyphId),
                default);

        public static bool MatchCoverageSequence(
            SkippingGlyphIterator iterator,
            CoverageTable[] coverageTable,
            int increment)
            => Match(
                increment,
                coverageTable,
                iterator,
                (component, data) => component.CoverageIndexOf(data.GlyphId) >= 0,
                default);

        public static bool ApplyChainedSequenceRule(SkippingGlyphIterator iterator, ChainedSequenceRuleTable rule)
        {
            if (rule.BacktrackSequence.Length > 0
                && !MatchSequence(iterator, -rule.BacktrackSequence.Length, rule.BacktrackSequence))
            {
                return false;
            }

            if (rule.InputSequence.Length > 0
                && !MatchSequence(iterator, 1, rule.InputSequence))
            {
                return false;
            }

            if (rule.LookaheadSequence.Length > 0
                && !MatchSequence(iterator, 1 + rule.InputSequence.Length, rule.LookaheadSequence))
            {
                return false;
            }

            return true;
        }

        public static bool ApplyChainedClassSequenceRule(
            SkippingGlyphIterator iterator,
            ChainedClassSequenceRuleTable rule,
            ClassDefinitionTable inputClassDefinitionTable,
            ClassDefinitionTable backtrackClassDefinitionTable,
            ClassDefinitionTable lookaheadClassDefinitionTable)
        {
            if (rule.BacktrackSequence.Length > 0
                && !MatchClassSequence(iterator, -rule.BacktrackSequence.Length, rule.BacktrackSequence, backtrackClassDefinitionTable))
            {
                return false;
            }

            if (rule.InputSequence.Length > 0 &&
                !MatchClassSequence(iterator, 1, rule.InputSequence, inputClassDefinitionTable))
            {
                return false;
            }

            if (rule.LookaheadSequence.Length > 0
                && !MatchClassSequence(iterator, 1 + rule.InputSequence.Length, rule.LookaheadSequence, lookaheadClassDefinitionTable))
            {
                return false;
            }

            return true;
        }

        public static bool CheckAllCoverages(
            FontMetrics fontMetrics,
            LookupFlags lookupFlags,
            IGlyphShapingCollection collection,
            ushort index,
            int count,
            CoverageTable[] input,
            CoverageTable[] backtrack,
            CoverageTable[] lookahead)
        {
            // Check that there are enough context glyphs.
            if (index - backtrack.Length < 0 || input.Length + lookahead.Length > count)
            {
                return false;
            }

            // Check all coverages: if any of them does not match, abort update.
            SkippingGlyphIterator iterator = new(fontMetrics, collection, index, lookupFlags);
            if (!MatchCoverageSequence(iterator, backtrack, -backtrack.Length))
            {
                return false;
            }

            if (!MatchCoverageSequence(iterator, input, 0))
            {
                return false;
            }

            if (!MatchCoverageSequence(iterator, lookahead, input.Length))
            {
                return false;
            }

            return true;
        }

        public static void ApplyAnchor(
            FontMetrics fontMetrics,
            GlyphPositioningCollection collection,
            ushort index,
            AnchorTable baseAnchor,
            MarkRecord markRecord,
            int baseGlyphIndex)
        {
            GlyphShapingData baseData = collection.GetGlyphShapingData(baseGlyphIndex);
            AnchorXY baseXY = baseAnchor.GetAnchor(fontMetrics, baseData, collection);

            GlyphShapingData markData = collection.GetGlyphShapingData(index);
            AnchorXY markXY = markRecord.MarkAnchorTable.GetAnchor(fontMetrics, markData, collection);

            markData.Bounds.X = baseXY.XCoordinate - markXY.XCoordinate;
            markData.Bounds.Y = baseXY.YCoordinate - markXY.YCoordinate;
            markData.MarkAttachment = baseGlyphIndex;
        }

        public static void ApplyPosition(
            GlyphPositioningCollection collection,
            ushort index,
            ValueRecord record)
        {
            GlyphShapingData current = collection.GetGlyphShapingData(index);
            current.Bounds.Width += record.XAdvance;
            current.Bounds.Height += record.YAdvance;
            current.Bounds.X += record.XPlacement;
            current.Bounds.Y += record.YPlacement;
        }

        public static bool IsMarkGlyph(FontMetrics fontMetrics, ushort glyphId, GlyphShapingData shapingData)
        {
            if (!fontMetrics.TryGetGlyphClass(glyphId, out GlyphClassDef? glyphClass) &&
                !CodePoint.IsMark(shapingData.CodePoint))
            {
                return false;
            }

            if (glyphClass != GlyphClassDef.MarkGlyph)
            {
                return false;
            }

            return true;
        }

        public static GlyphShapingClass GetGlyphShapingClass(FontMetrics fontMetrics, ushort glyphId, GlyphShapingData shapingData)
        {
            bool isMark;
            bool isBase;
            bool isLigature;
            ushort markAttachmentType = 0;
            if (fontMetrics.TryGetGlyphClass(glyphId, out GlyphClassDef? glyphClass))
            {
                isMark = glyphClass == GlyphClassDef.MarkGlyph;
                isBase = glyphClass == GlyphClassDef.BaseGlyph;
                isLigature = glyphClass == GlyphClassDef.LigatureGlyph;
                if (fontMetrics.TryGetMarkAttachmentClass(glyphId, out GlyphClassDef? markAttachmentClass))
                {
                    markAttachmentType = (ushort)markAttachmentClass;
                }
            }
            else
            {
                // TODO: We may have to store each codepoint. FontKit checks all.
                isMark = CodePoint.IsMark(shapingData.CodePoint);
                isBase = !isMark;
                isLigature = shapingData.CodePointCount > 1;
            }

            return new GlyphShapingClass(isMark, isBase, isLigature, markAttachmentType);
        }

        private static bool Match<T>(
            int increment,
            T[] sequence,
            SkippingGlyphIterator iterator,
            Func<T, GlyphShapingData, bool> condition,
            Span<int> matches)
        {
            ushort position = iterator.Index;
            ushort offset = iterator.Increment(increment);
            IGlyphShapingCollection collection = iterator.Collection;

            int i = 0;
            while (i < sequence.Length && i < MaxContextLength && offset < collection.Count)
            {
                if (!condition(sequence[i], collection.GetGlyphShapingData(offset)))
                {
                    break;
                }

                if (matches.Length == MaxContextLength)
                {
                    matches[i] = iterator.Index;
                }

                i++;
                offset = iterator.Next();
            }

            iterator.Index = position;
            return i == sequence.Length;
        }
    }
}
