﻿using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Sqlbi.Bravo.Infrastructure.Configuration.Options;
using Sqlbi.Bravo.Models;
using Sqlbi.Infrastructure;
using Sqlbi.Infrastructure.Configuration.Settings;
using System.Net.Mime;
using System.Text.Json;

namespace Sqlbi.Bravo.Controllers
{
    [Route("api/[action]")]
    [ApiController]
    public class ApplicationController : ControllerBase
    {
        private readonly IWritableOptions<UserSettings> _userOptions;

        public ApplicationController(IWritableOptions<UserSettings> userOptions)
        {
            _userOptions = userOptions;
            try
            {
                //_settings = options.Value;
            }
            catch /* (OptionsValidationException ex) */
            {
                throw;

                // TODO: logging
                //foreach (var failure in ex.Failures)
                //{
                //    _logger.LogError(failure);
                //}
            }
        }

        /// <summary>
        /// Get the application options
        /// </summary>
        /// <response code="200">Status200OK</response>
        [HttpGet]
        [ActionName("GetOptions")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(BravoOptions))]
        public IActionResult GetOptions()
        {
            JsonElement? customOptionsAsJsonElement = _userOptions.Value.CustomOptions is not null
                ? JsonSerializer.Deserialize<JsonElement>(_userOptions.Value.CustomOptions)
                : null;

            var options = new BravoOptions
            {
                TelemetryEnabled = _userOptions.Value.TelemetryEnabled,
                CustomOptions = customOptionsAsJsonElement
            };

            return Ok(options);
        }

        /// <summary>
        /// Update the application options
        /// </summary>
        /// <response code="200">Status200OK</response>
        [HttpPost]
        [ActionName("UpdateOptions")]
        [Consumes(MediaTypeNames.Application.Json)]
        public IActionResult UpdateOptions(BravoOptions options)
        {
            var customOptionsAsString = JsonSerializer.Serialize(options.CustomOptions);

            _userOptions.Update((o) =>
            {
                o.TelemetryEnabled = options.TelemetryEnabled;
                o.CustomOptions = customOptionsAsString;
            });

            return Ok();
        }
    }
}
