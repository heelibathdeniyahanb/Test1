﻿using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SolexCode.CRM.API.New.Data;
using SolexCode.CRM.API.New.DTOs;
using SolexCode.CRM.API.New.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SolexCode.CRM.API.New.Services;

namespace SolexCode.CRM.API.New.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ChatController : ControllerBase
    {
        private readonly DatabaseContext _context;
        private readonly ChatService _chatService; // Add a reference to the ChatService

        public ChatController(DatabaseContext context, ChatService chatService)
        {
            _context = context;
            _chatService = chatService;
        }

        // Get Chat Messages between Sender and Receiver within a Chat
        [HttpGet("chats/{chatId}/users/{senderId}/receivers/{receiverId}/messages")]
        public async Task<ActionResult<IEnumerable<ChatMessageDto>>> GetChatMessagesBetweenUsers(int chatId, int senderId, int receiverId)
        {
            try { 
            var messages = await _context.ChatMessages
                .Where(m => m.ChatId == chatId && ((m.SenderId == senderId && m.ReceiverId == receiverId) || (m.SenderId == receiverId && m.ReceiverId == senderId)))
                .Select(m => new ChatMessageDto
                {
                    Id = m.Id,
                    SenderId = m.SenderId,
                    ReceiverId = m.ReceiverId,
                    Content = m.Content,
                    Timestamp = m.Timestamp,
                    ChatId = m.ChatId
                })
                .ToListAsync();

            return Ok(messages);
        }
            catch (Exception ex)
            {
                // Log and handle exceptions appropriately
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }



        // Get Chat Messages
        [HttpGet("{chatId}/messages")]
        public async Task<ActionResult<IEnumerable<ChatMessageDto>>> GetChatMessages(int chatId)
        {
            var messages = await _context.ChatMessages
                .Where(m => m.ChatId == chatId)
                .Select(m => new ChatMessageDto
                {
                    Id = m.Id,
                    SenderId = m.SenderId,
                    ReceiverId = m.ReceiverId,
                    Content = m.Content,
                    Timestamp = m.Timestamp,
                    ChatId = m.ChatId
                })
                .ToListAsync();

            return Ok(messages);
        }

        // Get Chat Messages by User ID and Chat ID
        [HttpGet("{chatId}/users/{userId}/messages")]
        public async Task<ActionResult<IEnumerable<ChatMessageDto>>> GetUserChatMessages(int chatId, int userId)
        {
            var messages = await _context.ChatMessages
                .Where(m => m.ChatId == chatId && (m.SenderId == userId || m.ReceiverId == userId))
                .Select(m => new ChatMessageDto
                {
                    Id = m.Id,
                    SenderId = m.SenderId,
                    ReceiverId = m.ReceiverId,
                    Content = m.Content,
                    Timestamp = m.Timestamp,
                    ChatId = m.ChatId
                })
                .ToListAsync();

            return Ok(messages);
        }


        // Send Message
        [HttpPost("send")]
        public async Task<ActionResult> SendMessage([FromBody] ChatMessageDto messageDto)
        {
            try { 

            // Check if a chat exists between the sender and receiver
            var chat = await _context.Chats
                .Where(c => c.Participants.Any(p => p.UserId == messageDto.SenderId))
                .Where(c => c.Participants.Any(p => p.UserId == messageDto.ReceiverId))
                .FirstOrDefaultAsync();

            // If chat doesn't exist, create a new one
            if (chat == null)
            {
                // If chat doesn't exist, create a new one
                var participantIds = new List<int> { messageDto.SenderId, messageDto.ReceiverId };
                var newChatId = await _chatService.CreateNewChat(participantIds);
                messageDto.ChatId = newChatId; // Set the chatId in the messageDto
            }
            else
            {
                // If chat exists, set the chatId in the messageDto
                messageDto.ChatId = chat.Id;
            }

            // Check if Sender and Receiver exist
            var senderExists = await _context.Users.AnyAsync(u => u.Id == messageDto.SenderId);
            var receiverExists = await _context.Users.AnyAsync(u => u.Id == messageDto.ReceiverId);

            if (!senderExists)
            {
                return BadRequest("Sender does not exist.");
            }

            if (!receiverExists)
            {
                return BadRequest("Receiver does not exist.");
            }

            // Create new ChatMessage
            var chatMessage = new ChatMessage
            {
                SenderId = messageDto.SenderId,
                ReceiverId = messageDto.ReceiverId,
                Content = messageDto.Content,
                Timestamp = messageDto.Timestamp,
                ChatId = messageDto.ChatId
            };

            _context.ChatMessages.Add(chatMessage);

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex)
            {
                // Log the exception and return a 500 error
                // You can also inspect ex.InnerException for more details
                return StatusCode(500, "An error occurred while saving the message. See the inner exception for details.");
            }

            return Ok();
        }
            catch (Exception ex)
            {
                // Log and handle exceptions appropriately
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        // Get Chat Messages between Sender and Receiver
        [HttpGet("messages")]
        public async Task<ActionResult<IEnumerable<ChatMessageDto>>> GetMessagesBetweenUsers(int senderId, int receiverId)
        {
            try
            {
                var messages = await _context.ChatMessages
                    .Where(m => (m.SenderId == senderId && m.ReceiverId == receiverId) || (m.SenderId == receiverId && m.ReceiverId == senderId))
                    .Select(m => new ChatMessageDto
                    {
                        Id = m.Id,
                        SenderId = m.SenderId,
                        ReceiverId = m.ReceiverId,
                        Content = m.Content,
                        Timestamp = m.Timestamp,
                        ChatId = m.ChatId
                    })
                    .ToListAsync();

                return Ok(messages);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        // Get the latest message for a user in a specific chat
        [HttpGet("latestMessage")]
        public async Task<ActionResult<ChatMessageDto>> GetLatestMessageBetweenUsers(int senderId, int receiverId)
        {
            try
            {
                // Fetch the latest message where the user is either the sender or the receiver
                var latestMessage = await _context.ChatMessages
                    .Where(m => (m.SenderId == senderId && m.ReceiverId == receiverId) || (m.SenderId == receiverId && m.ReceiverId == senderId))
                    .OrderByDescending(m => m.Timestamp)
                    .Select(m => new ChatMessageDto
                    {
                        Id = m.Id,
                        SenderId = m.SenderId,
                        ReceiverId = m.ReceiverId,
                        Content = m.Content,
                        Timestamp = m.Timestamp,
                        ChatId = m.ChatId
                    })
                    .FirstOrDefaultAsync();

                if (latestMessage == null)
                {
                    return NotFound();
                }

                return Ok(latestMessage);
            }
            catch (Exception ex)
            {
                // Log the exception (using a logger, for example)
                // _logger.LogError(ex, "Error retrieving latest message between users {SenderId} and {ReceiverId}", senderId, receiverId);

                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }
    }
}

    

