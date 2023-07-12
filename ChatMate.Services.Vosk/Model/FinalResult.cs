﻿namespace ChatMate.Services.Vosk.Model;

[Serializable]
public class PartialResult
{
    public required string Partial { get; init; }
}

[Serializable]
public class FinalResult
{
    public ResultInfo[]? Result { get; init; }
    public required string Text { get; init; }
}

[Serializable]
public class ResultInfo
{
    public required double Conf { get; init; }
    public required double Start { get; init; }
    public required double End { get; init; }
    public required string Word { get; init; }
}