using BlueAmp.Helpers;
using Windows.Media.Audio;
using Xunit;

namespace BlueAmp.Tests;

public class ErrorTranslatorTests
{
    [Fact]
    public void Known_error_code_gives_a_specific_reason_instead_of_unknown()
    {
        // ERROR_DEVICE_NOT_CONNECTED
        string text = ErrorTranslator.Translate(
            AudioPlaybackConnectionOpenResultStatus.UnknownFailure, unchecked((int)0x8007048F));

        Assert.Contains("isn't reachable", text);
        Assert.DoesNotContain("unknown", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unrecognised_error_code_is_still_shown_to_the_user()
    {
        string text = ErrorTranslator.Translate(
            AudioPlaybackConnectionOpenResultStatus.UnknownFailure, unchecked((int)0x8ABCDEF1));

        Assert.Contains("0x8ABCDEF1", text);
    }

    [Fact]
    public void No_error_code_leaves_the_message_without_a_code_suffix()
    {
        string text = ErrorTranslator.Translate(AudioPlaybackConnectionOpenResultStatus.RequestTimedOut);

        Assert.Contains("timed out", text);
        Assert.DoesNotContain("0x", text);
    }

    [Fact]
    public void Success_is_never_described_as_a_failure()
    {
        Assert.Equal("Connected successfully.",
            ErrorTranslator.Translate(AudioPlaybackConnectionOpenResultStatus.Success, unchecked((int)0x8007048F)));
    }
}
