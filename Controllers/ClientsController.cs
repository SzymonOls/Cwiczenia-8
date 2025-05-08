using System.Data;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TaskAPI2.Models;

namespace TaskAPI2.Controllers;

[ApiController]
[Route("api/clients")]
public class ClientsController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public ClientsController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpGet("{id}/trips")]
    public IActionResult GetClientTrips(int id)
    {
        using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
        connection.Open();
        
        // czy klient istnieje
        var checkClient = new SqlCommand("SELECT 1 FROM Client WHERE IdClient = @id", connection);
        checkClient.Parameters.AddWithValue("@id", id);
        if (checkClient.ExecuteScalar() == null)
            return NotFound("Client doesnt exist");
        
        // wybiera wycieczki klienta
        var command = new SqlCommand(@"
            SELECT t.IdTrip, t.Name, t.Description, t.DateFrom, t.DateTo, ct.RegisteredAt, ct.PaymentDate
            FROM Client_Trip ct
            JOIN Trip t ON t.IdTrip = ct.IdTrip
            WHERE ct.IdClient = @id", connection);
        command.Parameters.AddWithValue("@id", id);
        
        
        using var reader = command.ExecuteReader();
        var trips = new List<object>();
        while (reader.Read())
        {
            int registeredAtInt = reader.GetInt32(5);
            DateTime? registeredAtDate = ParseIntDate(registeredAtInt);

            DateTime? paymentDate = null;
            if (!reader.IsDBNull(6))
            {
                int paymentDateInt = reader.GetInt32(6);
                paymentDate = ParseIntDate(paymentDateInt);
            }

            trips.Add(new
            {
                IdTrip = reader.GetInt32(0),
                Name = reader.GetString(1),
                Description = reader.GetString(2),
                DateFrom = reader.GetDateTime(3),
                DateTo = reader.GetDateTime(4),
                RegisteredAt = registeredAtDate,
                PaymentDate = paymentDate
            });
        }

        return Ok(trips);
    }

    private DateTime? ParseIntDate(int intDate)
    {
        var str = intDate.ToString();
        if (DateTime.TryParseExact(str, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var date))
        {
            return date;
        }
        return null;
    }
    
    
    [HttpPost]
    public IActionResult CreateClient([FromBody] ClientDto client)
    {
        if (string.IsNullOrWhiteSpace(client.FirstName) ||
            string.IsNullOrWhiteSpace(client.LastName) ||
            string.IsNullOrWhiteSpace(client.Email) ||
            string.IsNullOrWhiteSpace(client.Telephone) ||
            string.IsNullOrWhiteSpace(client.Pesel))
        {
            return BadRequest("All fields are required");
        }

        if (!Regex.IsMatch(client.Pesel, @"^\d{11}$"))
        {
            return BadRequest("Wrong pesel");
        }

        
        using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
        connection.Open();
        
        // tworzy klienta
        var createClient = new SqlCommand(@"
            INSERT INTO Client (FirstName, LastName, Email, Telephone, Pesel)
            OUTPUT INSERTED.IdClient
            VALUES (@FirstName, @LastName, @Email, @Telephone, @Pesel)", connection);

        createClient.Parameters.AddWithValue("@FirstName", client.FirstName);
        createClient.Parameters.AddWithValue("@LastName", client.LastName);
        createClient.Parameters.AddWithValue("@Email", client.Email);
        createClient.Parameters.AddWithValue("@Telephone", client.Telephone);
        createClient.Parameters.AddWithValue("@Pesel", client.Pesel);

        int newId = (int)createClient.ExecuteScalar();

        return StatusCode(201, new { id = newId });
    }
    
    
    
    [HttpPut("{id}/trips/{tripId}")]
    public IActionResult RegisterClientForTrip(int id, int tripId)
    {
        using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
        connection.Open();
        
        // wybiera klienta
        var checkClient = new SqlCommand("SELECT 1 FROM Client WHERE IdClient = @id", connection);
        checkClient.Parameters.AddWithValue("@id", id);
        if (checkClient.ExecuteScalar() == null)
            return NotFound("Client not found");
        
        // wybiera wycieczke
        var checkTrip = new SqlCommand("SELECT MaxPeople FROM Trip WHERE IdTrip = @tripId", connection);
        checkTrip.Parameters.AddWithValue("@tripId", tripId);
        var maxPeopleObj = checkTrip.ExecuteScalar();
        if (maxPeopleObj == null)
            return NotFound("Trip not found");

        int maxPeople = (int)maxPeopleObj;
        
        // sprawdza czy jest miejsce na wycieczce na nowego klienta
        var freeSpace = new SqlCommand("SELECT COUNT(*) FROM Client_Trip WHERE IdTrip = @tripId", connection);
        freeSpace.Parameters.AddWithValue("@tripId", tripId);
        int currentCount = (int)freeSpace.ExecuteScalar();

        if (currentCount >= maxPeople)
            return BadRequest("Maximum people reached");
        
        // sprawdza czy klient nie został już zapisany na wycieczke
        var alreadyRegistered = new SqlCommand("SELECT 1 FROM Client_Trip WHERE IdClient = @id AND IdTrip = @tripId", connection);
        alreadyRegistered.Parameters.AddWithValue("@id", id);
        alreadyRegistered.Parameters.AddWithValue("@tripId", tripId);
        if (alreadyRegistered.ExecuteScalar() != null)
            return BadRequest("Client is already registered for this trip");
        
        // dodaje rekord do tabeli
        var addRecord = new SqlCommand(@"
            INSERT INTO Client_Trip (IdClient, IdTrip, RegisteredAt)
            VALUES (@id, @tripId, @registeredAt)", connection);
        addRecord.Parameters.AddWithValue("@id", id);
        addRecord.Parameters.AddWithValue("@tripId", tripId);
        addRecord.Parameters.Add("@RegisteredAt", SqlDbType.DateTime).Value = DateTime.Now;


        addRecord.ExecuteNonQuery();

        return Ok("Client registered for the trip");
    }
    
    
    
    [HttpDelete("{id}/trips/{tripId}")]
    public IActionResult DeleteClientTrip(int id, int tripId)
    {
        using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
        connection.Open();
        
        // sprawdza czy klient jest zarejestroowany 
        var checkRegister = new SqlCommand("SELECT COUNT(*) FROM Client_Trip WHERE IdClient = @IdClient AND IdTrip = @IdTrip", connection);
        checkRegister.Parameters.AddWithValue("@IdClient", id);
        checkRegister.Parameters.AddWithValue("@IdTrip", tripId);

        var count = (int)checkRegister.ExecuteScalar();
        if (count == 0)
        {
            return NotFound(new { message = "Client not registered" });
        }
        
        // usuwa rekord z tabeli
        var deleteRecord = new SqlCommand("DELETE FROM Client_Trip WHERE IdClient = @IdClient AND IdTrip = @IdTrip", connection);
        deleteRecord.Parameters.AddWithValue("@IdClient", id);
        deleteRecord.Parameters.AddWithValue("@IdTrip", tripId);

        deleteRecord.ExecuteNonQuery();

        return Ok(new { message = "Registration deleted" });
    }
}