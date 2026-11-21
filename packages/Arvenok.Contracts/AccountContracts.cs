using System;

namespace Arvenok.Contracts;

public enum AccountOperation { Register, Login, List, Create, Delete, Enter, Return, Logout }
public sealed record AccountRequest(Guid CommandId, AccountOperation Operation, string Username = "", string Password = "",
    string Nickname = "", CharacterClass Class = CharacterClass.Warrior, Guid CharacterId = default);
public sealed record CharacterCard(Guid Id, string Nickname, CharacterClass Class, int Level);
public sealed record AccountReply(Guid CommandId, bool Accepted, string Message, bool Authenticated,
    CharacterCard[] Characters, bool InWorld = false);
