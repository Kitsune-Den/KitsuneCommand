using KitsuneCommand.Abstractions.Models;
using KitsuneCommand.Core;
using KitsuneCommand.Data.Entities;
using KitsuneCommand.Data.Repositories;

namespace KitsuneCommand.Services
{
    /// <summary>
    /// Subscribes to ChatMessageEvent on the event bus and persists every
    /// in-game chat message to the chat_records SQLite table.
    /// </summary>
    public class ChatPersistenceService
    {
        private readonly IChatRecordRepository _chatRepo;
        private readonly ModEventBus _eventBus;

        public ChatPersistenceService(IChatRecordRepository chatRepo, ModEventBus eventBus)
        {
            _chatRepo = chatRepo;
            _eventBus = eventBus;
        }

        public void Initialize()
        {
            _eventBus.Subscribe<ChatMessageEvent>(OnChatMessage);
            Log.Out("[KitsuneCommand] ChatPersistenceService initialized.");
        }

        private void OnChatMessage(ChatMessageEvent e)
        {
            try
            {
                _chatRepo.Insert(new ChatRecord
                {
                    PlayerId = e.PlayerId,
                    EntityId = e.EntityId,
                    SenderName = e.SenderName,
                    ChatType = (int)e.ChatType,
                    Message = e.Message
                });
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] Failed to persist chat message: {ex.Message}");
            }
        }
    }
}
