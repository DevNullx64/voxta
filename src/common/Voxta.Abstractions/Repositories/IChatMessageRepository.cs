﻿using Voxta.Abstractions.Model;

namespace Voxta.Abstractions.Repositories;

public interface IChatMessageRepository
{
    Task<ChatMessageData[]> GetChatMessagesAsync(string chatId, CancellationToken cancellationToken);
}