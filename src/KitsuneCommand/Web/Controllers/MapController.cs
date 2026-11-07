using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Web.Http;
using KitsuneCommand.Core;
using KitsuneCommand.Services;
using KitsuneCommand.Web.Auth;
using KitsuneCommand.Web.Models;

namespace KitsuneCommand.Web.Controllers
{
    /// <summary>
    /// Map tile and marker endpoints for the Leaflet-based world map.
    /// Tile endpoint is anonymous (just map imagery, not sensitive data).
    /// </summary>
    [RoutePrefix("api/map")]
    public class MapController : ApiController
    {
        private readonly MapTileRenderer _renderer;
        private readonly LivePlayerManager _playerManager;
        private readonly LandClaimService _landClaims;

        public MapController(MapTileRenderer renderer, LivePlayerManager playerManager, LandClaimService landClaims)
        {
            _renderer = renderer;
            _playerManager = playerManager;
            _landClaims = landClaims;
        }

        /// <summary>
        /// Returns a 256x256 PNG tile for the given zoom/x/y.
        /// Anonymous access — tile imagery is not sensitive.
        /// </summary>
        [HttpGet]
        [Route("tile/{z}/{x}/{y}")]
        [AllowAnonymous]
        public HttpResponseMessage GetTile(int z, int x, int y)
        {
            if (!_renderer.IsAvailable)
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent("Map renderer not available")
                };
            }

            var tileData = _renderer.RenderTile(z, x, y);
            if (tileData == null)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("Tile not found")
                };
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(tileData)
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            response.Headers.CacheControl = new CacheControlHeaderValue
            {
                Public = true,
                MaxAge = TimeSpan.FromHours(1)
            };

            return response;
        }

        /// <summary>
        /// Returns player positions as map markers.
        /// </summary>
        [HttpGet]
        [Route("markers")]
        [Authorize]
        public IHttpActionResult GetMarkers()
        {
            var players = _playerManager.GetAllOnline();
            var markers = players.Select(p => new
            {
                entityId = p.EntityId,
                name = p.PlayerName,
                x = p.PositionX,
                y = p.PositionY,
                z = p.PositionZ,
                type = "player"
            });

            return Ok(ApiResponse.Ok(markers));
        }

        /// <summary>
        /// Returns POI footprints (name, south-west corner, size) for the POI layer.
        /// </summary>
        [HttpGet]
        [Route("pois")]
        [Authorize]
        public IHttpActionResult GetPois()
        {
            var pois = _renderer.GetPois().Select(p => new
            {
                name = p.Name,
                x = p.X,
                z = p.Z,
                sizeX = p.SizeX,
                sizeZ = p.SizeZ
            });

            return Ok(ApiResponse.Ok(pois));
        }

        /// <summary>
        /// Every placed land claim block with its owner, plus the claim width.
        /// Staff only: on a PvP server this is a map of everyone's base.
        /// </summary>
        [HttpGet]
        [Route("landclaims")]
        [RoleAuthorize("admin", "moderator")]
        public IHttpActionResult GetLandClaims()
        {
            return Ok(ApiResponse.Ok(_landClaims.GetClaims()));
        }

        /// <summary>
        /// Returns map metadata (world size, bounds, zoom levels).
        /// </summary>
        [HttpGet]
        [Route("info")]
        [AllowAnonymous]
        public IHttpActionResult GetMapInfo()
        {
            if (!_renderer.IsAvailable)
            {
                return Ok(ApiResponse.Ok(new { isAvailable = false }));
            }

            return Ok(ApiResponse.Ok(_renderer.GetMapInfo()));
        }
    }
}
