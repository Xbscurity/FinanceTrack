using api.Constants;
using api.Dtos.User;
using api.Extensions;
using api.Queries;
using api.Services.Shared;
using api.Services.UserManagement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers.User
{
    /// <summary>
    /// Provides administrative endpoints for managing application users.
    /// </summary>
    /// <remarks>
    /// All endpoints require authentication and are accessible only to users
    /// assigned to the <c>Admin</c> role.
    /// </remarks>
    [Authorize(Roles = Roles.Admin)]
    [Route("api/users")]
    [ApiController]
    public class AdminUserManagementController : ControllerBase
    {
        private readonly IUserManagementService _userManagementService;

        /// <summary>
        /// Initializes a new instance of the <see cref="AdminUserManagementController"/> class.
        /// </summary>
        /// <param name="userManagementService">
        /// The service responsible for administrative user management operations.
        /// </param>
        public AdminUserManagementController(
            IUserManagementService userManagementService)
        {
            _userManagementService = userManagementService;
        }

        /// <summary>
        /// Retrieves a paginated list of users.
        /// </summary>
        /// <param name="query">
        /// The query parameters used for pagination and sorting.
        /// </param>
        /// <returns>
        /// A paginated collection of users.
        /// </returns>
        /// <response code="200">
        /// Returns the paginated collection of users.
        /// </response>
        [HttpGet]
        public async Task<ActionResult<PagedItems<UserManagementUserOutputDto>>> GetAllUsers(
            [FromQuery] UserManagementQuery query)
        {
            var result = await _userManagementService.GetAllUsersAsync(query);
            return result.ToActionResult(this);
        }

        /// <summary>
        /// Retrieves a user by identifier.
        /// </summary>
        /// <param name="id">
        /// The identifier of the user.
        /// </param>
        /// <returns>
        /// The requested user information.
        /// </returns>
        /// <response code="200">
        /// Returns the requested user.
        /// </response>
        /// <response code="404">
        /// The specified user was not found.
        /// </response>
        [HttpGet("{id}")]
        public async Task<ActionResult<UserManagementUserOutputDto>> GetById([FromRoute] string id)
        {
            var result = await _userManagementService.GetByIdAsync(id);

            return result.ToActionResult(this);
        }

        /// <summary>
        /// Updates the ban status of a user.
        /// </summary>
        /// <param name="userId">
        /// The identifier of the user whose ban status should be updated.
        /// </param>
        /// <param name="request">
        /// The request containing the new ban status.
        /// </param>
        /// <returns>
        /// The updated user ban status.
        /// </returns>
        /// <response code="200">
        /// The user ban status was successfully updated.
        /// </response>
        /// <response code="404">
        /// The specified user was not found.
        /// </response>
        [HttpPost("{userId}/ban-status")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<BanStatusOutputDto>> SetBanStatus(
            [FromRoute] string userId, [FromBody] BanStatusInputDto request)
        {
            var result = await _userManagementService.SetBanAsync(userId, request);
            return result.ToActionResult(this);
        }
    }
}