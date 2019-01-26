﻿using HavenSoft.Gen3Hex.Core.ViewModels.DataFormats;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace HavenSoft.Gen3Hex.Core.Models {
   public enum ElementContentType {
      Unknown,
      PCS,
   }

   public class ArrayRunElementSegment {
      public string Name { get; }
      public ElementContentType Type { get; }
      public int Length { get; }
      public ArrayRunElementSegment(string name, ElementContentType type, int length) => (Name, Type, Length) = (name, type, length);

      public string ToText(IReadOnlyList<byte> rawData, int offset) {
         switch (Type) {
            case ElementContentType.PCS:
               return PCSString.Convert(rawData, offset, Length);
            default:
               throw new NotImplementedException();
         }
      }
   }

   public class ArrayOffset {
      public int ElementIndex { get; }
      public int SegmentIndex { get; }
      public int SegmentStart { get; }
      public int SegmentOffset { get; }
      public ArrayOffset(int elementIndex, int segmentIndex, int segmentStart, int segmentOffset) {
         ElementIndex = elementIndex;
         SegmentIndex = segmentIndex;
         SegmentStart = segmentStart;
         SegmentOffset = segmentOffset;
      }
   }

   public class ArrayRun : BaseRun {
      public const char ExtendArray = '+';
      public const char ArrayStart = '[';
      public const char ArrayEnd = ']';

      private readonly IModel owner;

      // length in bytes of the entire array
      public override int Length { get; }

      public override string FormatString { get; }

      // number of elements in the array
      public int ElementCount { get; }

      // length of each element
      public int ElementLength { get; }

      public string LengthFromAnchor { get; }

      // composition of each element
      public IReadOnlyList<ArrayRunElementSegment> ElementContent { get; }

      private ArrayRun(IModel data, string format, int start, IReadOnlyList<int> pointerSources) : base(start, pointerSources) {
         owner = data;
         FormatString = format;
         var closeArray = format.LastIndexOf(ArrayEnd.ToString());
         if (!format.StartsWith(ArrayStart.ToString()) || closeArray == -1) throw new FormatException($"Array Content must be wrapped in {ArrayStart}{ArrayEnd}.");
         var segments = format.Substring(1, closeArray - 1);
         var length = format.Substring(closeArray + 1);
         ElementContent = ParseSegments(segments);
         if (ElementContent.Count == 0) throw new FormatException("Array Content must not be empty.");
         ElementLength = ElementContent.Sum(e => e.Length);

         if (length.Length == 0) {
            var nextRunStart = owner.GetNextRun(Start).Start;
            var byteLength = 0;
            var elementCount = 0;
            while (Start + byteLength + ElementLength <= nextRunStart && DataMatchesElementFormat(owner, Start + byteLength, ElementContent)) {
               byteLength += ElementLength;
               elementCount++;
            }
            ElementCount = elementCount;
         } else if (int.TryParse(length, out int result)) {
            // fixed length is easy
            ElementCount = result;
         } else {
            LengthFromAnchor = length;
            ElementCount = ParseLengthFromAnchor();
         }

         Length = ElementLength * ElementCount;
      }

      private ArrayRun(IModel data, string format, int start, int elementCount, IReadOnlyList<ArrayRunElementSegment> segments, IReadOnlyList<int> pointerSources) : base(start, pointerSources) {
         owner = data;
         FormatString = format;
         ElementContent = segments;
         ElementLength = ElementContent.Sum(e => e.Length);
         ElementCount = elementCount;
         LengthFromAnchor = string.Empty;
         Length = ElementLength * ElementCount;
      }

      public static bool TryParse(IModel data, string format, int start, IReadOnlyList<int> pointerSources, out ArrayRun self) {
         try {
            self = new ArrayRun(data, format, start, pointerSources);
         } catch {
            self = null;
            return false;
         }

         return true;
      }

      public static bool TrySearch(IModel data, string format, out ArrayRun self) {
         self = null;
         var closeArray = format.LastIndexOf(ArrayEnd.ToString());
         if (!format.StartsWith(ArrayStart.ToString()) || closeArray == -1) throw new FormatException($"Array Content must be wrapped in {ArrayStart}{ArrayEnd}");
         var segments = format.Substring(1, closeArray - 1);
         var length = format.Substring(closeArray + 1);
         var elementContent = ParseSegments(segments);
         if (elementContent.Count == 0) return false;
         var elementLength = elementContent.Sum(e => e.Length);

         int bestAddress = Pointer.NULL;
         int bestLength = 0;

         var run = data.GetNextRun(0);
         for (var nextRun = data.GetNextRun(run.Start+run.Length); run.Start < int.MaxValue; nextRun = data.GetNextRun(nextRun.Start + nextRun.Length)) {
            if (run is ArrayRun || run.PointerSources == null) {
               run = nextRun;
               continue;
            }

            int currentLength = 0;
            int currentAddress = run.Start;
            while (true) { // currentAddress < nextRun.Start
               if (DataMatchesElementFormat(data, currentAddress, elementContent)) {
                  currentLength++;
                  currentAddress += elementLength;
               } else {
                  break;
               }
            }
            if (bestLength < currentLength) {
               bestLength = currentLength;
               bestAddress = run.Start;
            }

            run = nextRun;
         }

         if (bestAddress == Pointer.NULL) return false;

         self = new ArrayRun(data, format + bestLength, bestAddress, bestLength, elementContent, data.GetNextRun(bestAddress).PointerSources);
         return true;
      }

      public override IDataFormat CreateDataFormat(IModel data, int index) {
         var offsets = ConvertByteOffsetToArrayOffset(index);

         if (ElementContent[offsets.SegmentIndex].Type == ElementContentType.PCS) {
            var fullString = PCSString.Convert(data, offsets.SegmentStart, ElementContent[offsets.SegmentIndex].Length);
            return PCSRun.CreatePCSFormat(data, offsets.SegmentStart, index, fullString);
         }

         throw new NotImplementedException();
      }

      public ArrayOffset ConvertByteOffsetToArrayOffset(int byteOffset) {
         var offset = byteOffset - Start;
         int elementIndex = offset / ElementLength;
         int elementOffset = offset % ElementLength;
         int segmentIndex = 0, segmentOffset = elementOffset;
         while (ElementContent[segmentIndex].Length < segmentOffset) {
            segmentOffset -= ElementContent[segmentIndex].Length; segmentIndex++;
         }
         return new ArrayOffset(elementIndex, segmentIndex, byteOffset - segmentOffset, segmentOffset);
      }

      public ArrayRun Append(int elementCount) {
         return new ArrayRun(owner, FormatString, Start, ElementCount + elementCount, ElementContent, PointerSources);
      }

      public void AppendTo(IReadOnlyList<byte> data, StringBuilder text) {
         for (int i = 0; i < ElementCount; i++) {
            var offset = Start + i * ElementLength;
            text.Append(ExtendArray);
            foreach (var segment in ElementContent) {
               text.Append(segment.ToText(data, offset));
               offset += segment.Length;
            }
            text.Append(Environment.NewLine);
         }
      }

      public IFormattedRun Move(int newStart) {
         return new ArrayRun(owner, FormatString, newStart, ElementCount, ElementContent, PointerSources);
      }

      protected override IFormattedRun Clone(IReadOnlyList<int> newPointerSources) {
         return new ArrayRun(owner, FormatString, Start, ElementCount, ElementContent, newPointerSources);
      }

      private static List<ArrayRunElementSegment> ParseSegments(string segments) {
         var list = new List<ArrayRunElementSegment>();
         segments = segments.Trim();
         while (segments.Length > 0) {
            int nameEnd = 0;
            while (nameEnd < segments.Length && char.IsLetterOrDigit(segments[nameEnd])) nameEnd++;
            var name = segments.Substring(0, nameEnd);
            if (name == string.Empty) throw new FormatException("expected name, but none was found: " + segments);
            segments = segments.Substring(nameEnd);
            var format = ElementContentType.Unknown;
            int formatLength = 0;
            int segmentLength = 0;
            if (segments.Length >= 2 && segments.Substring(0, 2) == "\"\"") {
               format = ElementContentType.PCS;
               formatLength = 2;
               while (formatLength < segments.Length && char.IsDigit(segments[formatLength])) formatLength++;
               segmentLength = int.Parse(segments.Substring(2, formatLength - 2));
            }
            if (format == ElementContentType.Unknown) throw new FormatException($"Could not parse format '{segments}'");
            segments = segments.Substring(formatLength).Trim();
            list.Add(new ArrayRunElementSegment(name, format, segmentLength));
         }

         return list;
      }

      private int ParseLengthFromAnchor() {
         // length is based on another array
         int address = owner.GetAddressFromAnchor(new DeltaModel(), -1, LengthFromAnchor);
         if (address == Pointer.NULL) {
            // the requested name was unknown... length is zero for now
            return 0;
         }

         var run = owner.GetNextRun(address) as ArrayRun;
         if (run == null || run.Start != address) {
            // the requested name was not an array, or did not start where anticipated
            // length is zero for now
            return 0;
         }

         return run.ElementCount;
      }

      private static bool DataMatchesElementFormat(IModel owner, int start, IReadOnlyList<ArrayRunElementSegment> segments) {
         foreach (var segment in segments) {
            if (start + segment.Length > owner.Count) return false;
            if (!DataMatchesSegmentFormat(owner, start, segment, segments.Count == 1)) return false;
            start += segment.Length;
         }
         return true;
      }

      private static bool DataMatchesSegmentFormat(IModel owner, int start, ArrayRunElementSegment segment, bool isSingleSegmentRun) {
         switch (segment.Type) {
            case ElementContentType.PCS:
               int readLength = PCSString.ReadString(owner, start, true, segment.Length);
               if (readLength == -1) return false;
               if (readLength > segment.Length) return false;
               if (Enumerable.Range(start, segment.Length).All(i => owner[i] == 0xFF)) return false;
               if (!Enumerable.Range(start + readLength, segment.Length - readLength).All(i => owner[i] == 0x00 || owner[i] == 0xFF)) return false;
               if (isSingleSegmentRun && owner.Count > start + segment.Length && owner[start + segment.Length] == 0x00) return false;
               return true;
            default:
               throw new NotImplementedException();
         }
      }
   }
}
