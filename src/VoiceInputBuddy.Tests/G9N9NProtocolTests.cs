// Copyright (C) 2026 Voice Input Buddy Contributors
// SPDX-License-Identifier: GPL-3.0-only

using VoiceInputBuddy.Core.Models;
using VoiceInputBuddy.Core.Protocols;
using Xunit;

namespace VoiceInputBuddy.Tests;

public class G9N9NProtocolTests
{
    [Fact]
    public void TryParseReport1_ParsesCustomButton_AtKnownIndex()
    {
        // Report ID 1, index 16 (0x10) = Custom button (0x0079)
        byte[] payload = [0x01, 0x10, 0x00];
        bool success = G9N9NProtocol.TryParseReport(payload, out var buttons);

        Assert.True(success);
        Assert.Single(buttons);
        Assert.Equal(RemoteButton.Custom, buttons[0]);
    }

    [Fact]
    public void TryParseReport1_ParsesDirectionKeys()
    {
        // Report ID 1, index 3 = Up (0x0042)
        byte[] payloadUp = [0x01, 0x03, 0x00];
        Assert.True(G9N9NProtocol.TryParseReport(payloadUp, out var upButtons));
        Assert.Single(upButtons);
        Assert.Equal(RemoteButton.Up, upButtons[0]);

        // Report ID 1, index 4 = Down (0x0043)
        byte[] payloadDown = [0x01, 0x04, 0x00];
        Assert.True(G9N9NProtocol.TryParseReport(payloadDown, out var downButtons));
        Assert.Single(downButtons);
        Assert.Equal(RemoteButton.Down, downButtons[0]);
    }

    [Fact]
    public void TryParseReport_ReturnsEmptyListOnReleaseReport_InsteadOfFailing()
    {
        // Key release with Report ID 1: [0x01, 0x00, 0x00]
        byte[] releaseReport1 = [0x01, 0x00, 0x00];
        Assert.True(G9N9NProtocol.TryParseReport(releaseReport1, out var buttons1));
        Assert.Empty(buttons1);

        // Key release with Report ID 2: [0x02, 0x00, 0x00]
        byte[] releaseReport2 = [0x02, 0x00, 0x00];
        Assert.True(G9N9NProtocol.TryParseReport(releaseReport2, out var buttons2));
        Assert.Empty(buttons2);

        // Key release without Report ID (2 bytes zero): [0x00, 0x00]
        byte[] releaseNoId = [0x00, 0x00];
        Assert.True(G9N9NProtocol.TryParseReport(releaseNoId, out var buttonsNoId));
        Assert.Empty(buttonsNoId);
    }
}
