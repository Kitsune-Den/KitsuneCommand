using System.Linq;
using System.Web.Http;
using KitsuneCommand.Data.Repositories;
using KitsuneCommand.Data.Entities;
using KitsuneCommand.Web.Auth;
using KitsuneCommand.Web.Models;

namespace KitsuneCommand.Web.Controllers
{
    /// <summary>
    /// Player metadata endpoints: custom tags, name colors, notes.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/players/metadata")]
    [RoleAuthorize("admin")]
    public class PlayerMetadataController : ApiController
    {
        private readonly IPlayerMetadataRepository _metadataRepo;

        public PlayerMetadataController(IPlayerMetadataRepository metadataRepo)
        {
            _metadataRepo = metadataRepo;
        }

        /// <summary>
        /// Get all player metadata as a dictionary keyed by playerId.
        /// </summary>
        [HttpGet]
        [Route("")]
        public IHttpActionResult GetAllMetadata()
        {
            var all = _metadataRepo.GetAll();
            var dict = all.ToDictionary(m => m.PlayerId);
            return Ok(ApiResponse.Ok(dict));
        }

        /// <summary>
        /// Get metadata for a specific player.
        /// </summary>
        [HttpGet]
        [Route("{playerId}")]
        public IHttpActionResult GetMetadata(string playerId)
        {
            var metadata = _metadataRepo.GetByPlayerId(playerId);
            return Ok(ApiResponse.Ok(metadata));
        }

        /// <summary>
        /// Create or update metadata for a player.
        /// </summary>
        [HttpPut]
        [Route("{playerId}")]
        public IHttpActionResult UpdateMetadata(string playerId, [FromBody] UpdatePlayerMetadataRequest request)
        {
            if (request == null)
                return BadRequest("Request body is required.");

            var metadata = new PlayerMetadata
            {
                PlayerId = playerId,
                NameColor = request.NameColor,
                CustomTag = request.CustomTag,
                Notes = request.Notes
            };

            _metadataRepo.Upsert(metadata);
            return Ok(ApiResponse.Ok(metadata));
        }
    }
}
