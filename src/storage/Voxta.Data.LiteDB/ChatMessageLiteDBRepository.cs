﻿
using Voxta.Abstractions.Repositories;
using LiteDB;
using Voxta.Abstractions.Model;

namespace Voxta.Data.LiteDB;

public class ChatMessageLiteDBRepository : IChatMessageRepository
{
    private readonly ILiteCollection<ChatMessageData> _chatMessagesCollection;

    public ChatMessageLiteDBRepository(ILiteDatabase db)
    {
        _chatMessagesCollection = db.GetCollection<ChatMessageData>();
    }
    
    public Task<ChatMessageData[]> GetChatMessagesAsync(string chatId, CancellationToken cancellationToken)
    {
        var messages = _chatMessagesCollection.Query()
            .Where(c => c.ChatId == chatId)
            .OrderBy(c => c.Timestamp)
            .ToArray();

        return Task.FromResult(messages);
    }
}