using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace TaskAPI2.Controllers;

[ApiController]
[Route("api/trips")]
public class TripsController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public TripsController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpGet]
    public IActionResult GetAllTrips()
    {
        var trips = new List<object>();

        using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
        connection.Open();

        var command = new SqlCommand(@"
            SELECT t.IdTrip, t.Name, t.Description, t.DateFrom, t.DateTo, t.MaxPeople,
                   c.Name AS CountryName
            FROM dbo.Trip t
            LEFT JOIN Country_Trip ct ON t.IdTrip = ct.IdTrip
            LEFT JOIN Country c ON ct.IdCountry = c.IdCountry
            ORDER BY t.IdTrip
        ", connection);

        using var reader = command.ExecuteReader();

        var tripDict = new Dictionary<int, dynamic>();

        while (reader.Read())
        {
            var idTrip = reader.GetInt32(0);

            if (!tripDict.ContainsKey(idTrip))
            {
                tripDict[idTrip] = new
                {
                    IdTrip = idTrip,
                    Name = reader.GetString(1),
                    Description = reader.GetString(2),
                    DateFrom = reader.GetDateTime(3),
                    DateTo = reader.GetDateTime(4),
                    MaxPeople = reader.GetInt32(5),
                    Countries = new List<string>()
                };
            }

            if (!reader.IsDBNull(6))
            {
                ((List<string>)tripDict[idTrip].Countries).Add(reader.GetString(6));
            }
        }

        trips.AddRange(tripDict.Values);

        return Ok(trips);
    }
}