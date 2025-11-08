using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Api.Models;
using FIBRADIS.Application.Models;
using FIBRADIS.Application.Ports;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace FIBRADIS.Api.Controllers;

[ApiController]
[Route("v1/news")]
public sealed class NewsController : ControllerBase
{
    private readonly INewsRepository _newsRepository;
    private readonly ILogger<NewsController> _logger;

    public NewsController(INewsRepository newsRepository, ILogger<NewsController> logger)
    {
        _newsRepository = newsRepository ?? throw new ArgumentNullException(nameof(newsRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [HttpGet]
    [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any)]
    [ProducesResponseType(typeof(IEnumerable<NewsItemResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetNewsAsync(CancellationToken cancellationToken)
    {
        var news = await _newsRepository.GetPublishedAsync(null, cancellationToken).ConfigureAwait(false);
        var response = news.Select(NewsItemResponse.FromModel).ToArray();
        _logger.LogDebug("Served {Count} published news items", response.Length);
        return Ok(response);
    }

    [HttpGet("{ticker}")]
    [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any)]
    [ProducesResponseType(typeof(IEnumerable<NewsItemResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetNewsByTickerAsync([FromRoute] string ticker, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ticker))
        {
            return BadRequest("Ticker is required");
        }

        var normalized = ticker.Trim();
        var news = await _newsRepository.GetPublishedAsync(normalized, cancellationToken).ConfigureAwait(false);
        var response = news.Select(NewsItemResponse.FromModel).ToArray();
        return Ok(response);
    }
}
