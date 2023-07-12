﻿using ChatMate.Abstractions.Model;
using ChatMate.Common;
using Microsoft.Extensions.Logging;

namespace ChatMate.Core;

public partial class ChatSession
{
    public void SendReady()
    {
        Enqueue(SendReadyAsync);
    }

    private async ValueTask SendReadyAsync(CancellationToken cancellationToken)
    {
        var thinkingSpeechUrls = new List<string>(_chatSessionData.ThinkingSpeech?.Length ?? 0);
        if (_chatSessionData.ThinkingSpeech != null)
        {
            foreach (var thinkingSpeech in _chatSessionData.ThinkingSpeech)
            {
                var thinkingSpeechUrl = await _speechGenerator.CreateSpeechAsync(thinkingSpeech, $"think_{Crypto.CreateCryptographicallySecureGuid()}", true, cancellationToken);
                if (thinkingSpeechUrl != null)
                    thinkingSpeechUrls.Add(thinkingSpeechUrl);
            }
        }

        await _tunnel.SendAsync(new ServerReadyMessage
        {
            ChatId = _chatSessionData.ChatId,
            ThinkingSpeechUrls = thinkingSpeechUrls.ToArray(),
                
        }, cancellationToken);
        
        _logger.LogInformation("Chat ready!");

        if (_chatSessionData.Greeting != null)
        {
            var reply = ChatMessageData.FromGen(_chatSessionData.CharacterName, _chatSessionData.Greeting);
            _chatSessionData.Messages.Add(reply);
            await SendReplyWithSpeechAsync(reply, "greet", cancellationToken);
        }
    }
}