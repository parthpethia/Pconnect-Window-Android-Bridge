using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using Pconnect.Agent.Services;
using Xunit;

namespace Pconnect.Agent.Tests;

public sealed class ReplaceAllTests
{
    private sealed class MockKeyboardInjector : KeyboardInjector
    {
        public bool SendCtrlACalled { get; set; }
        public bool SendCtrlVCalled { get; set; }
        public bool SendUnicodeCalled { get; set; }
        public bool SendBackspacesCalled { get; set; }
        public string? SentUnicodeText { get; set; }
        public int SentBackspacesCount { get; set; }
        public bool PasteTextSafelyReturnValue { get; set; } = true;
        public bool PasteTextSafelyThrows { get; set; } = false;

        public override void SendCtrlA()
        {
            SendCtrlACalled = true;
        }

        public override void SendCtrlV()
        {
            SendCtrlVCalled = true;
        }

        public override void SendUnicode(string text)
        {
            SendUnicodeCalled = true;
            SentUnicodeText = text;
        }

        public override void SendBackspaces(int count)
        {
            SendBackspacesCalled = true;
            SentBackspacesCount = count;
        }

        public override bool PasteTextSafely(string text)
        {
            if (PasteTextSafelyThrows)
            {
                throw new Exception("Clipboard access error");
            }
            return PasteTextSafelyReturnValue;
        }
    }

    private sealed class ClipboardTestKeyboardInjector : KeyboardInjector
    {
        public override void SendCtrlV()
        {
            // No-op to avoid actual OS keystroke injection during tests
        }
    }

    [Fact]
    public void ReplaceAllText_SendsCtrlAAndPaste_OnSuccess()
    {
        var mock = new MockKeyboardInjector
        {
            PasteTextSafelyReturnValue = true
        };
        var actions = new PcActions(mock);

        actions.ReplaceAllText("test text");

        Assert.True(mock.SendCtrlACalled);
        Assert.False(mock.SendBackspacesCalled);
        Assert.False(mock.SendUnicodeCalled);
    }

    [Fact]
    public void ReplaceAllText_FallsBackToTypedInput_OnPasteFailure()
    {
        var mock = new MockKeyboardInjector
        {
            PasteTextSafelyReturnValue = false
        };
        var actions = new PcActions(mock);

        actions.ReplaceAllText("fallback text");

        Assert.True(mock.SendCtrlACalled);
        Assert.True(mock.SendBackspacesCalled);
        Assert.Equal(1, mock.SentBackspacesCount);
        Assert.True(mock.SendUnicodeCalled);
        Assert.Equal("fallback text", mock.SentUnicodeText);
    }

    [Fact]
    public void ReplaceAllText_FallsBackToTypedInput_OnPasteException()
    {
        var mock = new MockKeyboardInjector
        {
            PasteTextSafelyThrows = true
        };
        var actions = new PcActions(mock);

        actions.ReplaceAllText("fallback text exception");

        Assert.True(mock.SendCtrlACalled);
        Assert.True(mock.SendBackspacesCalled);
        Assert.Equal(1, mock.SentBackspacesCount);
        Assert.True(mock.SendUnicodeCalled);
        Assert.Equal("fallback text exception", mock.SentUnicodeText);
    }

    [Fact]
    public void PasteTextSafely_RestoresClipboardText_STAThread()
    {
        var exception = null as Exception;
        var thread = new Thread(() =>
        {
            try
            {
                Clipboard.Clear();
                var originalText = "Original Clipboard Text " + Guid.NewGuid().ToString();
                Clipboard.SetText(originalText);

                var backup = Clipboard.GetDataObject();
                if (backup != null)
                {
                    foreach (var fmt in backup.GetFormats(false))
                    {
                        try
                        {
                            var val = backup.GetData(fmt);
                            Console.WriteLine($"[TEST DIAG] Format: {fmt}, Type: {val?.GetType().FullName ?? "null"}, Value: {val}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[TEST DIAG] Format: {fmt} failed to read: {ex.Message}");
                        }
                    }
                }

                var testInjector = new ClipboardTestKeyboardInjector();
                bool result = testInjector.PasteTextSafely("Temporary Replace Text");

                Assert.True(result);
                var afterText = Clipboard.GetText();
                Console.WriteLine($"[TEST DIAG] Text after restore: '{afterText}'");
                Assert.Equal(originalText, afterText);
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception != null)
        {
            throw exception;
        }
    }

    [Fact]
    public void PasteTextSafely_RestoresNonTextFormats_STAThread()
    {
        var exception = null as Exception;
        var thread = new Thread(() =>
        {
            try
            {
                Clipboard.Clear();
                var dataObject = new DataObject();
                var customFormat = "MyCustomFormat";
                var customValue = new byte[] { 1, 2, 3, 4 };
                dataObject.SetData(customFormat, customValue);
                Clipboard.SetDataObject(dataObject, true);

                var testInjector = new ClipboardTestKeyboardInjector();
                bool result = testInjector.PasteTextSafely("Temporary Replace Text");

                Assert.True(result);

                var restoredData = Clipboard.GetDataObject();
                Assert.NotNull(restoredData);
                Assert.True(restoredData.GetDataPresent(customFormat));
                var restoredValue = restoredData.GetData(customFormat) as byte[];
                Assert.NotNull(restoredValue);
                Assert.Equal(customValue, restoredValue);
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception != null)
        {
            throw exception;
        }
    }
}
