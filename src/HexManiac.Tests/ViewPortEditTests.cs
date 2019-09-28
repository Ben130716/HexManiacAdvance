﻿using HavenSoft.HexManiac.Core;
using HavenSoft.HexManiac.Core.Models;
using HavenSoft.HexManiac.Core.Models.Runs;
using HavenSoft.HexManiac.Core.ViewModels;
using HavenSoft.HexManiac.Core.ViewModels.DataFormats;
using System;
using System.Linq;
using Xunit;

namespace HavenSoft.HexManiac.Tests {
   public class ViewPortEditTests : BaseViewModelTestClass {
      [Fact]
      public void CanEditData() {
         ViewPort.SelectionStart = new Point(2, 2);
         ViewPort.Edit("A");
         ViewPort.Edit("D");

         Assert.Equal(0xAD, ViewPort[2, 2].Value);
         Assert.Equal(new Point(3, 2), ViewPort.SelectionStart);
      }

      [Fact]
      public void CanEditMultipleBytesInARow() {
         ViewPort.SelectionStart = new Point(2, 2);
         ViewPort.Edit("DEADBEEF");

         Assert.Equal(0xDE, ViewPort[2, 2].Value);
         Assert.Equal(0xAD, ViewPort[3, 2].Value);
         Assert.Equal(0xBE, ViewPort[4, 2].Value);
         Assert.Equal(0xEF, ViewPort[5, 2].Value);
      }

      [Fact]
      public void CanUndoEdit() {
         ViewPort.SelectionStart = new Point(2, 2);
         ViewPort.Edit("AD");
         ViewPort.Undo.Execute();

         Assert.Equal(0x00, ViewPort[2, 2].Value);
      }

      [Fact]
      public void UndoCanReverseMultipleByteChanges() {
         ViewPort.SelectionStart = new Point(2, 2);
         ViewPort.Edit("DEADBEEF");
         ViewPort.Undo.Execute();

         Assert.Equal(0x00, ViewPort[2, 2].Value);
         Assert.Equal(0x00, ViewPort[2, 3].Value);
         Assert.Equal(0x00, ViewPort[2, 4].Value);
         Assert.Equal(0x00, ViewPort[2, 5].Value);
      }

      [Fact]
      public void MovingBetweenChangesCausesSeparateUndo() {
         ViewPort.SelectionStart = new Point(2, 2);
         ViewPort.Edit("01");
         ViewPort.SelectionStart = new Point(2, 3);
         ViewPort.Edit("02");
         ViewPort.Undo.Execute();

         Assert.Equal(0x01, ViewPort[2, 2].Value);
         Assert.Equal(0x00, ViewPort[2, 3].Value);
      }

      [Fact]
      public void CanRedo() {
         ViewPort.Edit("DEADBEEF");
         Assert.False(ViewPort.Redo.CanExecute(null));

         ViewPort.Undo.Execute();
         Assert.True(ViewPort.Redo.CanExecute(null));

         ViewPort.Redo.Execute();
         Assert.False(ViewPort.Redo.CanExecute(null));
      }

      [Fact]
      public void UndoFixesCorrectDataAfterScroll() {
         ViewPort.SelectionStart = new Point(2, 2);
         ViewPort.Edit("FF");

         ViewPort.SelectionStart = new Point(2, 2);
         ViewPort.Edit("EE");
         ViewPort.Scroll.Execute(Direction.Down);
         ViewPort.Undo.Execute();

         Assert.Equal(1, ViewPort.ScrollValue);
         Assert.Equal(0xFF, ViewPort[2, 1].Value);
      }

      [Fact]
      public void EditMovesSelection() {
         ViewPort.SelectionStart = new Point(2, 2);
         ViewPort.Edit("FF");

         Assert.Equal(new Point(3, 2), ViewPort.SelectionStart);
      }

      [Fact]
      public void UndoDoesNotMoveSelection() {
         ViewPort.SelectionStart = new Point(2, 2);
         ViewPort.Edit("FF");
         ViewPort.Undo.Execute();

         Assert.Equal(new Point(3, 2), ViewPort.SelectionStart);
      }

      [Fact]
      public void UndoCanCauseScrolling() {
         ViewPort.SelectionStart = new Point(0, 0);
         ViewPort.Edit("FF");
         ViewPort.Scroll.Execute(Direction.Down);
         ViewPort.Undo.Execute();

         Assert.Equal(0, ViewPort.ScrollValue);
      }

      [Fact]
      public void SingleCharacterEditChangesToUnderEditFormat() {
         ViewPort.SelectionStart = new Point(0, 2);
         ViewPort.Edit("F");

         Assert.IsType<UnderEdit>(ViewPort[0, 2].Format);
         Assert.Equal("F", ((UnderEdit)ViewPort[0, 2].Format).CurrentText);
      }

      [Fact]
      public void UnsupportedCharacterRevertsChangeWithoutAddingUndoOperation() {
         ViewPort.SelectionStart = new Point(0, 2);
         ViewPort.Edit("F");
         ViewPort.Edit("|");

         Assert.IsType<None>(ViewPort[0, 2].Format);
         Assert.Equal(new Point(0, 2), ViewPort.SelectionStart);
         Assert.Equal(0, ViewPort[0, 2].Value);
         Assert.False(ViewPort.Undo.CanExecute(null));
      }

      [Fact]
      public void EscapeRevertsChangeWithoutAddingUndoOperation() {
         ViewPort.SelectionStart = new Point(0, 2);
         ViewPort.Edit("F");
         ViewPort.Edit(ConsoleKey.Escape);

         Assert.IsType<None>(ViewPort[0, 2].Format);
         Assert.Equal(new Point(0, 2), ViewPort.SelectionStart);
         Assert.Equal(0, ViewPort[0, 2].Value);
         Assert.False(ViewPort.Undo.CanExecute(null));
      }

      [Fact]
      public void SelectionChangeDuringEditNotifiesCollectionChange() {
         int collectionNotifications = 0;
         ViewPort.CollectionChanged += (sender, e) => collectionNotifications++;

         ViewPort.SelectionStart = new Point(4, 4);
         ViewPort.Edit("F");
         Assert.Equal(1, collectionNotifications);

         ViewPort.MoveSelectionStart.Execute(Direction.Up);
         Assert.Equal(3, collectionNotifications); // should have been notified since the visual data changed.
      }

      [Fact]
      public void UndoNotifiesCollectionChange() {
         int collectionNotifications = 0;
         ViewPort.CollectionChanged += (sender, e) => collectionNotifications++;

         ViewPort.Edit("0102030405");
         collectionNotifications = 0;
         ViewPort.Undo.Execute();

         Assert.Equal(1, collectionNotifications);
      }

      [Fact]
      public void UndoRestoresOriginalDataFormat() {
         var originalFormat = ViewPort[0, 0].Format;
         ViewPort.Edit("ff");
         ViewPort.Undo.Execute();

         Assert.IsType(originalFormat.GetType(), ViewPort[0, 0].Format);
      }

      [Fact]
      public void CanEnterDataAfterLastByte() {
         ViewPort.SelectionStart = new Point(0, 4);
         ViewPort.Edit("00");

         Assert.NotEqual(Undefined.Instance, ViewPort[0, 4].Format);
         Assert.Equal(new Point(1, 4), ViewPort.SelectionStart);
      }

      [Fact]
      public void CanClearData() {
         ViewPort.SelectionStart = new Point(0, 0);
         ViewPort.SelectionEnd = new Point(3, 3);
         ViewPort.Clear.Execute();

         Assert.Equal(0xFF, ViewPort[2, 2].Value);
         Assert.True(ViewPort.Save.CanExecute(new StubFileSystem()));
      }

      [Fact]
      public void CanCopyData() {
         var fileSystem = new StubFileSystem();

         ViewPort.Edit("Cafe Babe");
         ViewPort.SelectionStart = new Point(0, 0);
         ViewPort.SelectionEnd = new Point(3, 0);
         ViewPort.Copy.Execute(fileSystem);

         Assert.Equal("CA FE BA BE", fileSystem.CopyText);
      }

      [Fact]
      public void CanBackspaceOnEdits() {
         for (byte i = 0; i < 255; i++) Data[i] = i;

         ViewPort.SelectionStart = new Point(4, 4); // current value: 0x44
         ViewPort.Edit("C");
         ViewPort.Edit(ConsoleKey.Backspace);

         var editFormat = (UnderEdit)ViewPort[4, 4].Format;
         Assert.Equal(string.Empty, editFormat.CurrentText);

         ViewPort.MoveSelectionStart.Execute(Direction.Down); // any movement should change the value based on what's left in the cell
         Assert.Equal(0xFF, ViewPort[4, 4].Value); // note that the cell was empty, so it got the 'empty' value of FF
      }

      [Fact]
      public void BackspaceBeforeEditChangesPreviousCurrentCell() {
         for (byte i = 0; i < 255; i++) Data[i] = i;

         ViewPort.SelectionStart = new Point(4, 4); // current value: 0x44
         ViewPort.Edit(ConsoleKey.Backspace);
         Assert.Equal(new Point(4, 4), ViewPort.SelectionStart);

         ViewPort.Edit(ConsoleKey.Backspace); // if I hit an arrow key now, it'll give up on the edit and just make the value something reasonable
         ViewPort.Edit(ConsoleKey.Backspace); // but since I hit backspace, it commits the erasure and starts erasing the next cell
         Assert.Equal(0xFF, ViewPort[4, 4].Value);
      }

      [Fact]
      public void ClearRemovesFormats() {
         ViewPort.Edit("<000100>");
         ViewPort.SelectionStart = new Point(2, 0);
         ViewPort.Clear.Execute();

         Assert.Equal(int.MaxValue, Model.GetNextRun(0).Start);
      }

      [Fact]
      public void TypingIntoPointerAutoInsertsStartOfPointer() {
         ViewPort.Edit("<000100>");
         ViewPort.SelectionStart = new Point(0, 0);

         ViewPort.Edit("000060");
         ViewPort.Edit(ConsoleKey.Enter);

         Assert.Equal(0x60, Model[0]);
      }

      [Fact]
      public void TypingTwoHexDigitsIntoPointerWithSpaceCanRemoveFormat() {
         ViewPort.Edit("<000100>");
         ViewPort.SelectionStart = new Point(0, 0);

         ViewPort.Edit("20 ");

         Assert.Equal(0x20, Model[0]);
         Assert.NotInRange(Model.GetNextRun(0).Start, 0, 0x200);
      }

      [Fact]
      public void UnderEditCellsKnowTheirEditLength() {
         ViewPort.Edit("^array[a: b. c. d<>]4 ");

         ViewPort.SelectionStart = new Point(0, 0);
         ViewPort.Edit("2");
         Assert.Equal(2, ((UnderEdit)ViewPort[0, 0].Format).EditWidth);

         ViewPort.SelectionStart = new Point(6, 0);
         ViewPort.Edit("2");
         Assert.Equal(4, ((UnderEdit)ViewPort[4, 0].Format).EditWidth);

         ViewPort.SelectionStart = new Point(8, 6);
         ViewPort.Edit("^");
         Assert.Equal(4, ((UnderEdit)ViewPort[8, 6].Format).EditWidth);
      }

      [Fact]
      public void CanCopyPasteBitArray() {
         var fileSystem = new StubFileSystem();
         CreateTextTable("names", 0x10, "Adam", "Bob", "Carl", "David", "Evan", "Fred", "Greg", "Holly", "Iggy", "Jay", "Kelly", "Lucy", "Mary", "Nate", "Ogre", "Phil"); // 0x60
         CreateEnumTable("enums", 0x80, "names", Enumerable.Range(0, 12).ToArray());
         CreateBitArrayTable("bitArray", 0xB0, "enums", 0x01, 0x02, 0x04, 0x08, 0x10, 0x20, 0x40, 0x80);

         // verify that copy works
         ViewPort.Goto.Execute("00");
         ViewPort.SelectionStart = new Point(0x4, 0xB);
         ViewPort.Copy.Execute(fileSystem);
         Assert.Contains("04 00", fileSystem.CopyText);

         // verify that direct entry works
         ViewPort.Edit("0100");
         Assert.Equal(new Point(0x6, 0xB), ViewPort.SelectionStart);
         Assert.Equal(0x0001, Model.ReadMultiByteValue(0xB4, 2));

         // verify that direct entry works with spaces
         ViewPort.Edit("02 03");
         Assert.Equal(0x0302, Model.ReadMultiByteValue(0xB6, 2));
      }

      [Fact]
      public void PlmRunsContainingNamesWithSpacesCanBeCorrectlyEditedWithTheTextTool() {
         var tool = ViewPort.Tools.StringTool;
         var delta = new ModelDelta();
         CreateTextTable(EggMoveRun.MoveNamesTable, 0x00, "Option0", "Option1", "Option2 More", "Option3 More", "Option4");
         for (int i = 0; i < 4; i++) Model.WriteMultiByteValue(0x50 + i * 2, 2, delta, 0x0201);
         Model.WriteMultiByteValue(0x58, 2, new ModelDelta(), 0xFFFF);
         ViewPort.Goto.Execute("50");
         ViewPort.Edit("^sample`plm` ");

         Assert.Equal(
@"1 Option1
1 Option1
1 Option1
1 Option1", tool.Content);

         tool.Content =
@"1 Option1
2 ""Option2 More""
2 Option4
2 Option4";

         Assert.Equal(
@"1 Option1
2 ""Option2 More""
2 Option4
2 Option4", tool.Content);

         var plm = (PLMRun)Model.GetNextRun(0x50);
         Assert.Equal(10, plm.Length);

         Assert.Equal(0x0201, Model.ReadMultiByteValue(0x50, 2));
         Assert.Equal(0x0402, Model.ReadMultiByteValue(0x52, 2));
         Assert.Equal(0x0404, Model.ReadMultiByteValue(0x54, 2));
         Assert.Equal(0x0404, Model.ReadMultiByteValue(0x56, 2));
      }
   }
}
