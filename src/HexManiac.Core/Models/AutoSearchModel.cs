﻿using HavenSoft.HexManiac.Core.Models.Runs;
using HavenSoft.HexManiac.Core.ViewModels.DataFormats;
using System.Diagnostics;
using System.Linq;
using static HavenSoft.HexManiac.Core.Models.Runs.ArrayRun;

namespace HavenSoft.HexManiac.Core.Models {
   public class AutoSearchModel : PokemonModel {
      public const string
         Ruby = "AXVE",
         Sapphire = "AXPE",
         Emerald = "BPEE",
         FireRed = "BPRE",
         LeafGreen = "BPGE";

      private readonly string gameCode;
      private readonly ModelDelta noChangeDelta = new NoDataChangeDeltaModel();

      /// <summary>
      /// The first 0x100 bytes of the GBA rom is always the header.
      /// The next 0x100 bytes contains some tables and some startup code, but nothing interesting to point to.
      /// Choosing 0x200 might prevent us from seeing an actual anchor, but it will also remove a bunch
      ///      of false positives and keep us from getting conflicts with the RomName (see DecodeHeader).
      /// </summary>
      public override int EarliestAllowedAnchor => 0x200;

      public AutoSearchModel(byte[] data, StoredMetadata metadata = null) : base(data, metadata) {
         if (metadata != null) return;

         gameCode = string.Concat(Enumerable.Range(0xAC, 4).Select(i => ((char)data[i]).ToString()));

         // in vanilla emerald, this pointer isn't four-byte aligned
         // it's at the very front of the ROM, so if there's no metadata we can be pretty sure that the pointer is still there
         if (gameCode == Emerald && data[0x1C3] == 0x08) ObserveRunWritten(noChangeDelta, new PointerRun(0x1C0));

         var gamesToDecode = new[] { Ruby, Sapphire, Emerald, FireRed, LeafGreen };
         if (gamesToDecode.Contains(gameCode)) {
            DecodeHeader();
            DecodeNameArrays();
            DecodeDataArrays();
         }
      }

      private void DecodeHeader() {
         ObserveAnchorWritten(noChangeDelta, "GameTitle", new AsciiRun(0xA0, 12));
         ObserveAnchorWritten(noChangeDelta, "GameCode", new AsciiRun(0xAC, 4));
         ObserveAnchorWritten(noChangeDelta, "MakerCode", new AsciiRun(0xB0, 2));

         if (gameCode != Ruby && gameCode != Sapphire) {
            ObserveAnchorWritten(noChangeDelta, "RomName", new AsciiRun(0x108, 0x20));
         }
      }

      private void DecodeNameArrays() {
         // movenames
         if (TrySearch(this, noChangeDelta, "[name\"\"13]", out var movenames)) {
            ObserveAnchorWritten(noChangeDelta, "movenames", movenames);
         }

         // pokenames
         if (TrySearch(this, noChangeDelta, "[name\"\"11]", out var pokenames)) {
            ObserveAnchorWritten(noChangeDelta, "pokenames", pokenames);
         }

         // abilitynames / trainer names
         if (gameCode == Ruby || gameCode == Sapphire || gameCode == Emerald) {
            if (TrySearch(this, noChangeDelta, "[name\"\"13]", out var abilitynames)) {
               ObserveAnchorWritten(noChangeDelta, "abilitynames", abilitynames);
            }
            if (TrySearch(this, noChangeDelta, "[name\"\"13]", out var trainerclassnames)) {
               ObserveAnchorWritten(noChangeDelta, "trainerclassnames", trainerclassnames);
            }
         } else {
            if (TrySearch(this, noChangeDelta, "[name\"\"13]", out var trainerclassnames)) {
               ObserveAnchorWritten(noChangeDelta, "trainerclassnames", trainerclassnames);
            }
            if (TrySearch(this, noChangeDelta, "[name\"\"13]", out var abilitynames)) {
               ObserveAnchorWritten(noChangeDelta, "abilitynames", abilitynames);
            }
         }

         // types
         if (TrySearch(this, noChangeDelta, "^[name\"\"7]", out var typenames)) { // the type names are sometimes pointed to directly, instead of in the array
            ObserveAnchorWritten(noChangeDelta, "types", typenames);
         }
      }

      private void DecodeDataArrays() {
         if (TrySearch(this, noChangeDelta, "[name\"\"14 index: price: holdeffect: description<> keyitemvalue. bagkeyitem. pocket. type. fieldeffect<> battleusage:: battleeffect<> battleextra::]", out var itemdata)) {
            ObserveAnchorWritten(noChangeDelta, "items", itemdata);
         }

         if (TrySearch(this, noChangeDelta, "[hp. attack. def. speed. spatk. spdef. type1.types type2.types catchRate. baseExp. evs: item1:items item2:items genderratio. steps2hatch. basehappiness. growthrate. egg1. egg2. ability1.abilitynames ability2.abilitynames runrate. unknown. padding:]pokenames", out var pokestatdata)) {
            ObserveAnchorWritten(noChangeDelta, "pokestats", pokestatdata);
         }

         // the first abilityDescriptions pointer is directly after the first abilityNames pointer
         var abilityNamesAddress = GetAddressFromAnchor(noChangeDelta, -1, "abilitynames");
         if (abilityNamesAddress != Pointer.NULL) {
            var firstPointerToAbilityNames = GetNextAnchor(abilityNamesAddress).PointerSources?.FirstOrDefault() ?? Pointer.NULL;
            if (firstPointerToAbilityNames != Pointer.NULL) {
               var firstPointerToAbilityDescriptions = firstPointerToAbilityNames + 4;
               var abilityDescriptionsAddress = ReadPointer(firstPointerToAbilityDescriptions);
               var existingRun = GetNextAnchor(abilityDescriptionsAddress);
               if (!(existingRun is ArrayRun) && existingRun.Start == abilityDescriptionsAddress) {
                  var error = TryParse(this, "[description<>]abilitynames", existingRun.Start, existingRun.PointerSources, out var abilityDescriptions);
                  if (!error.HasError) ObserveAnchorWritten(noChangeDelta, "abilitydescriptions", abilityDescriptions);
               }
            }
         }

         // @3D4294 ^itemicons[image<> palette<>]items
         // @4886E8 ^movedescriptions[description<>]354
         // @250C04 ^movedata[effect. power. type.types accuracy. pp. effectAccuracy. target. priority. more::]movenames
      }
   }
}
