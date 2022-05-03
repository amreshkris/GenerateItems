using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using GenerateItems;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

namespace GenerateItems
{
    class Program
    {
        private static readonly string cartContainer = "cart-container";
        private static readonly string buyerContainer = "buyer-container";
        // private static readonly string leasesContainer = "leases-cart";
        // private static readonly string partitionKeyPath = "/id";
        private static readonly int numberOfOrders = 10;
        static async Task Main(string[] args)
        {
            try
            {
                IConfigurationRoot configuration = new ConfigurationBuilder()
                  .AddJsonFile("appSettings.json", optional: true, reloadOnChange: true)
                  .Build();

                string endpoint = configuration["EndPointUrl"];
                if (string.IsNullOrEmpty(endpoint))
                {
                    throw new ArgumentNullException
                    ("Please specify a valid endpoint in the appSettings.json");
                }

                string authKey = configuration["AuthorizationKey"];
                if (string.IsNullOrEmpty(authKey) || string.Equals(authKey, "Super secret key"))
                {
                    throw new ArgumentException("Please specify a valid AuthorizationKey in the appSettings.json");
                }

                Console.WriteLine("Choose an option below to generate seed data");
                Console.WriteLine("1-Generate one time feed");
                Console.WriteLine("2-Generate continuous feed");

                var feedGenerationMode = Console.ReadLine();

                using (CosmosClient client = new CosmosClient(endpoint, authKey))
                {
                    //Console.WriteLine("Generating 10 items that will be picked up by the delegate...");
                    await Program.GenerateSeedData("changefeed-basic", client, feedGenerationMode);
                }
                Console.WriteLine("\nInitialized containers");
                Console.ReadKey();
            }
            catch
            {
                throw;
            }
        }      
     

        private static async Task GenerateSeedData(string databaseId, CosmosClient client, string feedGenerationMode)
        {
            // Initialize database & containers, delete if exsits and create db,containers
            await Program.InitializeAsync(databaseId, client);

            // Initialize Buyer container
            Container buyerContainer = client.GetContainer(databaseId, Program.buyerContainer);
            var cwd = Directory.GetCurrentDirectory() + "\\SeedData\\Buyers.json";
            var buyerJsonPath = Path.Combine(Directory.GetCurrentDirectory(), "\\SeedData\\Buyers.json");
            List<Buyer> buyers = JsonConvert.DeserializeObject<List<Buyer>>(File.ReadAllText(cwd)).ToList();
            foreach (var buyer in buyers)
            {
                await buyerContainer.CreateItemAsync<Buyer>(buyer, new PartitionKey(buyer.BuyerId));
            }


            // Initialize cart container
            Container cartContainer = client.GetContainer(databaseId, Program.cartContainer);
            await Task.Delay(500);
            var random = new Random();

            if (feedGenerationMode.Equals("1"))
            {
                for (int i = 0; i < Program.numberOfOrders; i++)
                {
                    await createRandomeCarts(buyers, cartContainer, random);
                }
            }
            else
            {
                while (!(Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Escape))
                {
                    Console.Write("*****");
                    await Task.Delay(1000);
                    await createRandomeCarts(buyers, cartContainer, random);
                }
            }

            static async Task createRandomeCarts(List<Buyer> buyers, Container cartContainer, Random random)
            {
                string id = Guid.NewGuid().ToString();
                var randomBookedServices = GetRandomServices();

                var computeCartParams = ComputeOrderStatus(randomBookedServices);
                var cart = new Cart()
                {
                    CartId = id,
                    Id = Guid.NewGuid().ToString(),
                    BuyerId = buyers[random.Next(0, buyers.Count)].BuyerId,
                    BookedServices = randomBookedServices,
                    OrderStatus = computeCartParams.cartStatus,
                    Total = computeCartParams.total
                };
                await cartContainer.CreateItemAsync<Cart>(cart, new PartitionKey(cart.CartId));
            }
        }

        private static async Task InitializeAsync(string databaseId, CosmosClient client)
        {
            Database database;
            // Recreate database
            try
            {
                database = await client.GetDatabase(databaseId).ReadAsync();
                await database.DeleteAsync();
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
            }

            database = await client.CreateDatabaseAsync(databaseId);

            await database.CreateContainerIfNotExistsAsync(new ContainerProperties(Program.cartContainer, "/cartId"));

            await database.CreateContainerIfNotExistsAsync(new ContainerProperties(Program.buyerContainer, "/buyerId"));
        }

        #region "Private Helper Methods"
        /// <summary>
        /// Compute Random Order Status 
        /// </summary>
        /// <param name="cartStatus"></param>
        /// <param name="bookedServices"></param>
        /// <returns></returns>
        private static (CartStatus cartStatus, decimal total) ComputeOrderStatus(List<CartItem> bookedServices)
        {

            decimal totalCost = 0;
            foreach (var item in bookedServices)
            {
                totalCost += item.UnitPrice;
            }
            var cartStatus = totalCost > 20 ? CartStatus.Abondoned : CartStatus.Completed;
            var total = totalCost;
            return (cartStatus, total);
        }

        /// <summary>
        /// Get Random Availed Services by the customer
        /// </summary>
        /// <returns></returns>
        private static List<CartItem> GetRandomServices()
        {
            var randomServices = new List<CartItem>();
            var randomCount = new Random().Next(1, Enum.GetNames(typeof(HomeServices)).Length);
            var randomService = new Random();
            for (int itemIndex = 0; itemIndex < randomCount; itemIndex++)
            {
                randomServices.Add(new CartItem()
                {
                    Id = Guid.NewGuid().ToString(),
                    Service = randomService.NextEnum<HomeServices>(),
                    UnitPrice = 10 * randomCount
                });
            }
            return randomServices;
        }

        #endregion
    }
}
