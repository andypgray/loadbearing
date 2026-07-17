using Meridian.Clearance;
using Meridian.Domain;
using Meridian.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class BookingsController(
    IBookingRepository bookings,
    IClearanceGateway clearance,
    IClock clock) : ControllerBase
{
    private static readonly string[] SampleContainers = ["CSQU3054383", "MSCU5285725", "TGHU8065427"];

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateBookingRequest request)
    {
        if (request.CutoffUtc <= clock.UtcNow) return BadRequest($"Cutoff {request.CutoffUtc:u} has already passed.");

        List<string> rejected = [];
        foreach (string containerNumber in request.ContainerNumbers)
            if (!clearance.IsValidContainerNumber(containerNumber))
                rejected.Add(containerNumber);

        if (rejected.Count > 0) return BadRequest($"Rejected container numbers: {string.Join(", ", rejected)}");

        Booking? existing = await bookings.Get(request.Reference);
        if (existing is not null) return Conflict($"Booking {request.Reference} already exists.");

        var booking = new Booking
        {
            Reference = request.Reference,
            CustomerName = request.CustomerName,
            Lane = request.Lane,
            ContainerNumbers = request.ContainerNumbers,
            CutoffUtc = request.CutoffUtc
        };
        await bookings.Add(booking);

        string clearanceDocument = clearance.BuildClearanceDocument(request.Reference, request.ContainerNumbers);
        return Ok(new BookingConfirmation(request.Reference, clearanceDocument));
    }

    [HttpPost("validate-containers")]
    public IActionResult ValidateContainers([FromBody] IReadOnlyList<string> containerNumbers)
    {
        List<ContainerValidationResult> results = [];
        foreach (string containerNumber in containerNumbers)
        {
            bool isValid = clearance.IsValidContainerNumber(containerNumber);
            results.Add(new ContainerValidationResult(containerNumber, isValid));
        }

        return Ok(results);
    }

    [HttpGet("sample")]
    public IActionResult Sample()
    {
        var sample = new CreateBookingRequest(
            "SAMPLE-0001",
            "Acme Importers",
            "CNSHA-USLAX",
            SampleContainers,
            clock.UtcNow.AddHours(48));

        return Ok(sample);
    }
}