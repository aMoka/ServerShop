using System;
using System.IO;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Timers;

using Mono.Data.Sqlite;
using MySql.Data.MySqlClient;

using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.DB;
using Wolfje.Plugins.SEconomy;

namespace ServerShop
{
	[ApiVersion(1, 16)]
	public class SS : TerrariaPlugin
	{
		private IDbConnection db;
		public List<ShopItem> Inventory = new List<ShopItem>();
		public List<ShopItem> RestockItems = new List<ShopItem>();
		public List<string> Regions = new List<string>();
		public Timer RestockTimer = new Timer(1000);

		#region Info
		public override string Name { get { return "ServerShop"; } }
		public override string Author { get { return "aMoka"; } }
		public override string Description { get { return "It's like trading yesterday's regrets."; } }
		public override Version Version { get { return Assembly.GetExecutingAssembly().GetName().Version; } }

		public SS(Main game)
			: base(game)
		{
			Order = 1;
			TShock.Initialized += Start;
		}
		#endregion

		#region Start
		private void Start()
		{
			SetupDb();
			ReadDb();
			RestockTimer.Elapsed += new ElapsedEventHandler(Restock);
			RestockTimer.Enabled = true;
		}
		#endregion

		#region SetupDb
		private void SetupDb()
		{
			switch (TShock.Config.StorageType.ToLower())
			{
				case "sqlite":
					db = new SqliteConnection(string.Format("uri=file://{0},Version=3",
						Path.Combine(TShock.SavePath, "ServerShop.sqlite")));
					break;
				case "mysql":
					try
					{
						var host = TShock.Config.MySqlHost.Split(':');
						db = new MySqlConnection
						{
							ConnectionString = String.Format("Server={0}; Port={1}; Database={2}; Uid={3}; Pwd={4}",
								host[0],
								host.Length == 1 ? "3306" : host[1],
								TShock.Config.MySqlDbName,
								TShock.Config.MySqlUsername,
								TShock.Config.MySqlPassword
								)
						};
					}
					catch (MySqlException x)
					{
						Log.Error(x.ToString());
						throw new Exception("MySQL not setup correctly.");
					}
					break;
				default:
					throw new Exception("Invalid storage type.");
			}

			var sqlCreator = new SqlTableCreator(db,
				db.GetSqlType() == SqlType.Sqlite
				? (IQueryBuilder)new SqliteQueryCreator()
				: new MysqlQueryCreator());

			var inv = new SqlTable("Inventory",
				new SqlColumn("ID", MySqlDbType.Int32),
				new SqlColumn("Price", MySqlDbType.Int32),
				new SqlColumn("Stock", MySqlDbType.Int32),
				new SqlColumn("MaxStock", MySqlDbType.Int32),
				new SqlColumn("RestockTime", MySqlDbType.Int32),
				new SqlColumn("RestockType", MySqlDbType.Int32)
				);

			var reg = new SqlTable("ShopRegions",
				new SqlColumn("Region", MySqlDbType.String)
				);

			sqlCreator.EnsureExists(inv);
			sqlCreator.EnsureExists(reg);
		}
		#endregion

		#region ReadDb
		private void ReadDb()
		{
			using (var reader = db.QueryReader("SELECT * FROM Inventory"))
			{
				while (reader.Read())
				{
					int id = reader.Get<int>("ID");
					int price = reader.Get<int>("Price");
					int stock = reader.Get<int>("Stock");
					int maxStock = reader.Get<int>("MaxStock");
					int restockTime = reader.Get<int>("RestockTime");
					int restockType = reader.Get<int>("RestockType");
 
					if (!Inventory.ContainsItem(id))
						Inventory.Add(new ShopItem(id, price, stock, maxStock, restockTime, restockType));
 
					if (restockTime > 0)
						RestockItems.Add(new ShopItem(id, price, stock, maxStock, restockTime, restockType));
				}
			}
 
			using (var reader = db.QueryReader("SELECT * FROM ShopRegions"))
			{
				while (reader.Read())
				{
					string region = reader.Get<string>("Region");
 
					if (!Regions.Contains(region))
						Regions.Add(region);
				}
			}
		}
		#endregion

		#region Initialize
		public override void Initialize()
		{
			#region Commands
			Commands.ChatCommands.Add(new Command("ss.player", Shop, "sshop", "ss"));
			Commands.ChatCommands.Add(new Command("ss.admin", AdminShop, "ssa"));
			#endregion
		}
		#endregion

		#region Dispose
		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				TShock.Initialized -= Start;
			}
			base.Dispose(disposing);
		}
		#endregion

		#region Restock
		private void Restock(object sender, ElapsedEventArgs args)
		{
			foreach (ShopItem item in RestockItems)
			{
				if (item.restockTime < 0)
					continue;

				if (item.restockTime > 0)
				{
					item.restockTime--;
					continue;
				}

				var itm = Inventory.GetShopItem(item.id);
				item.restockTime = itm.restockTime;
				if (itm.maxStock == -1)
					continue;

				switch (itm.restockType)
				{
					case -1:
						if (itm.stock < Math.Floor((double)item.maxStock / 2))
						{
							itm.stock = (int)Math.Floor((double)item.maxStock / 2);
						}
						break;
					case 0:
						if (itm.stock != Math.Floor((double)item.maxStock / 2))
						{
							itm.stock = (int)Math.Floor((double)item.maxStock / 2);
						}
						break;
					case 1:
						if (itm.stock > Math.Floor((double)item.maxStock / 2))
						{
							itm.stock = (int)Math.Floor((double)item.maxStock / 2);
						}
						break;
				}
				db.Query("UPDATE Inventory SET Stock = @0 WHERE ID = @1", itm.stock, itm.id);
			}
		}
		#endregion

		#region Commands
		#region Shop
		private void Shop(CommandArgs args)
		{
			#region PreSubCmd
			var account = SEconomyPlugin.Instance.GetBankAccount(args.Player.UserAccountName);
			Item itm = new Item();
			ShopItem item = new ShopItem();
			string itemName;
			List<Item> matchedItems = new List<Item>();
			int amount = 1;
			Money price;
			Money sellprice;

			if (args.Parameters.Count < 2)
			{
				args.Player.SendValidShopUsage();
				return;
			}

			matchedItems = TShock.Utils.GetItemByIdOrName(string.Join(" ", args.Parameters.GetRange(1, args.Parameters.Count - 2)));
			if (matchedItems.Count == 0)
			{
				args.Player.SendErrorMessage("Invalid item!");
				return;
			}
			else if (matchedItems.Count > 1)
			{
				TShock.Utils.SendMultipleMatchError(args.Player, matchedItems.Select(i => i.name));
				return;
			}
			item = Inventory.GetShopItem(matchedItems[0].netID);
			itemName = TShock.Utils.GetItemById(item.id).name;

			if (args.Parameters.Count > 2)
			{
				if (args.Parameters.Last().ToLower() == "all")
				{
					amount = item.stock;
				}
				else if (!Int32.TryParse(args.Parameters.Last(), out amount))
				{
					args.Player.SendErrorMessage("Invalid amount!");
					return;
				}
			}
			#endregion

			string subcmd = args.Parameters[0].ToLower();

			switch (subcmd)
			{
				#region buy
				case "buy":
					int times;
					if (!args.Player.IsInShopRegion(Regions))
						return;

					if (!args.Player.InventorySlotAvailable)
					{
						args.Player.SendErrorMessage("Insufficient inventory space!");
						return;
					}
					if (amount > item.stock)
					{
						args.Player.SendErrorMessage("There are only {0} {1}(s) left in stock!", item.stock, itemName);
						return;
					}
					price = (item.price * amount < 1) ? 1 * amount : item.price * amount;
					if (price > account.Balance)
					{
						args.Player.SendErrorMessage("You are short {0} {1} from buying {2} {3}(s)!",
							price - account.Balance, Money.CurrencyName, amount, itemName);
						return;
					}
					account.TransferTo(SEconomyPlugin.Instance.WorldAccount, 
						price, 
						Wolfje.Plugins.SEconomy.Journal.BankAccountTransferOptions.AnnounceToSender,
						String.Format("buying {0} {1}(s) from the ServerShop.", amount, itemName),
						"ServerShop Purchase."
						);
					Inventory.GetShopItem(item.id).stock -= amount;
					db.Query("UPDATE Inventory SET Stock = @0 WHERE ID = @1", Inventory.GetShopItem(item.id).stock, item.id);
					itm.SetDefaults(item.id);
					times = (int)Math.Ceiling((double)amount / itm.maxStack);
					if (amount > itm.maxStack)
					{
						for (int i = 0; i < times; i++)
						{
							args.Player.GiveItem(itm.netID, itm.name, itm.width, itm.height, itm.maxStack);
						}
					}
					args.Player.GiveItem(itm.netID, itm.name, itm.width, itm.height, amount - (itm.maxStack * (times - 1)));
					return;
				#endregion
				#region sell
				case "sell":
					if (!args.Player.IsInShopRegion(Regions))
						return;

					var inventory = args.TPlayer.inventory;
					List<int> itemLocations = new List<int>();
					int amt = 0;

					for (int i = 50; i >= 0; i--)
					{
						if (inventory[i].netID == item.id)
						{
							amt += inventory[i].stack;
							itemLocations.Add(i);
						}
					}

					if (args.Parameters[args.Parameters.Count - 1].ToLower() == "all")
					{
						amount = 0;
						foreach (var i in itemLocations)
						{
							amount += inventory[i].stack;
						}
					}

					if (amt < amount)
					{
						args.Player.SendErrorMessage("You do not own {0} {1}(s)!", amount, itemName);
						return;
					}
					if (item.maxStock != -1 && item.stock + amount > item.maxStock)
					{
						args.Player.SendErrorMessage("The maximum you can sell is {0} {1}(s).", item.maxStock - item.stock, itemName);
						return;
					}
					if (item.price <= 5)
					{
						price = item.price * amount;
					}
					else
						price = item.price * amount / 5;

					SEconomyPlugin.Instance.WorldAccount.TransferTo(account,
						price,
						Wolfje.Plugins.SEconomy.Journal.BankAccountTransferOptions.AnnounceToReceiver,
						String.Format("selling {0} {1}(s) from the ServerShop.", amount, itemName),
						"ServerShop Sale."
						);
					
					Inventory.GetShopItem(item.id).stock += amount;
					db.Query("UPDATE Inventory SET Stock = @0 WHERE ID = @1", Inventory.GetShopItem(item.id).stock, item.id);

					foreach (var loc in itemLocations)
					{
						if (amount == 0)
							break;

						if (inventory[loc].stack < amount)
						{
							amount -= inventory[loc].stack;
							inventory[loc].stack = 0;
						}
						else
						{
							inventory[loc].stack -= amount;
							amount = 0;
						}
						NetMessage.SendData((int)PacketTypes.PlayerSlot, -1, -1, "", args.Player.Index, loc);
					}
					return;
				#endregion
				#region search
				case "search":
					price = item.price;
					sellprice = price / 5;
					if (sellprice == 0)
					{
						sellprice = 1;
					}

					args.Player.SendInfoMessage("Item: {0}; Buy Price: {1}; Sell Price: {2}; Stock: {3}; MaxStock: {4}; Restock Time: {5}",
						itemName, 
						price.ToLongString(), 
						sellprice.ToLongString(),
						item.stock, 
						item.maxStock == -1 ? "Infinity" : item.maxStock.ToString(), 
						item.restockTime == -1 ? "None" : item.restockTime.ToString()
						);
					return;
				#endregion
				default:
					args.Player.SendValidShopUsage();
					return;
			}
		}
		#endregion

		#region AdminShop
		private void AdminShop(CommandArgs args)
		{
			if (args.Parameters.Count < 1)
			{
				args.Player.SendValidAdminShopUsage();
				return;
			}

			switch (args.Parameters[0].ToLower())
			{
				#region populatedb
				case "populatedb":
					if (args.Parameters.Count < 2)
					{
						args.Player.SendInfoMessage("You are about to populate the shop database with default values!");
						args.Player.SendInfoMessage("If you have done this at least once already, it may not be wise to again.");
						args.Player.SendInfoMessage("To proceed, type \"/ssa populatedb -confirm\"");
						return;
					}
					if (args.Parameters[1].ToLower() == "-confirm")
					{
						for (int i = -48; i < 2743; i++)
						{
							if (i == 0)
								continue;

							int value = 1;
							int maxstock = 1;
							Item item = new Item();
							if (i > 0)
								item.SetDefaults(i);
							else
								item.SetDefaults(TShock.Utils.GetItemById(i).name);

							if (item.value != 0)
							{
								value = item.value;
							}
							if (item.maxStack != 0)
							{
								maxstock = item.maxStack;
							}
							int stock = (int)Math.Floor((double)maxstock / 2);
							if (maxstock == 1)
							{
								maxstock = -1;
							}
							try
							{
								db.Query("INSERT INTO Inventory (ID, Price, Stock, MaxStock, RestockTime, RestockType)"
									+ " VALUES (@0, @1, @2, @3, @4, @5)",
									i, value, stock, maxstock, -1, -1);
							}
							catch (Exception ex)
							{
								Log.Error(ex.ToString());
							}
						}
						RestockItems.Clear();
						Inventory.Clear();
						ReadDb();
						args.Player.SendSuccessMessage("Database successfully populated.");
						Log.Info("ServerShop Inventory successfully populated.");
						return;
					}
					args.Player.SendValidAdminShopUsage();
					return;
				#endregion
				#region balance
				case "balance":
					if (args.Parameters.Count < 2)
					{
						args.Player.SendValidBalanceUsage();
						return;
					}

					Dictionary<int, int> balancedStocks = new Dictionary<int, int>();
					switch (args.Parameters[1].ToLower())
					{
						case "-shortage":
							foreach (ShopItem item in Inventory)
							{
								if (item.maxStock == -1)
									continue;

								if (item.stock < Math.Floor((double)item.maxStock / 2))
								{
									item.stock = (int)Math.Floor((double)item.maxStock / 2);
									balancedStocks.Add(item.id, item.stock);
								}
							}
							break;

						case "-default":
							foreach (ShopItem item in Inventory)
							{
								if (item.maxStock == -1)
									continue;

								if (item.stock != Math.Floor((double)item.maxStock / 2))
								{
									item.stock = (int)Math.Floor((double)item.maxStock / 2);
									balancedStocks.Add(item.id, item.stock);
								}
							}
							break;
						case "-surplus":
							foreach (ShopItem item in Inventory)
							{
								if (item.maxStock == -1)
									continue;

								if (item.stock > Math.Floor((double)item.maxStock / 2))
								{
									item.stock = (int)Math.Floor((double)item.maxStock / 2);
									balancedStocks.Add(item.id, item.stock);
								}
							}
							break;
						default:
							args.Player.SendValidBalanceUsage();
							return;
					}
					foreach (var item in balancedStocks)
						db.Query("UPDATE Inventory SET Stock = @1 WHERE ID = @0", item.Key, item.Value);
					args.Player.SendSuccessMessage("Balanced {0} items in ServerShop Inventory.", balancedStocks.Count);
					return;
				#endregion
				#region modify
				case "modify":
					if (args.Parameters.Count < 4)
					{
						args.Player.SendValidModifyUsage();
						return;
					}

					List<Item> matchedItems = new List<Item>();
					ShopItem itm = new ShopItem();
					string itemName;
					int number;

					if (!Int32.TryParse(args.Parameters[3], out number))
					{
						args.Player.SendErrorMessage("Invalid number!");
						return;
					}
					matchedItems = TShock.Utils.GetItemByIdOrName(string.Join(" ", args.Parameters.GetRange(1, args.Parameters.Count - 3)));
					if (matchedItems.Count == 0)
					{
						args.Player.SendErrorMessage("Invalid item!");
						return;
					}
					else if (matchedItems.Count > 1)
					{
						TShock.Utils.SendMultipleMatchError(args.Player, matchedItems.Select(i => i.name));
						return;
					}
					itm = Inventory.GetShopItem(matchedItems[0].netID);
					itemName = TShock.Utils.GetItemById(itm.id).name;

					switch (args.Parameters[2].ToLower())
					{
						case "price":
							Money money = number;
							itm.price = number;
							db.Query("UPDATE Inventory SET Price = @1 WHERE ID = @0", itm.id, itm.price);
							args.Player.SendSuccessMessage("Changed {0}'s price to {1}.", itemName, money.ToLongString());
							return;
						case "stock":
							itm.stock = number;
							db.Query("UPDATE Inventory SET Stock = @1 WHERE ID = @0", itm.id, itm.stock);
							args.Player.SendSuccessMessage("Changed {0}'s stock to {1}.", itemName, itm.stock);
							return;
						case "maxstock":
							itm.maxStock = number;
							db.Query("UPDATE Inventory SET MaxStock = @1 WHERE ID = @0", itm.id, itm.maxStock);
							args.Player.SendSuccessMessage("Changed {0}'s max stock to {1}.", itemName, itm.maxStock);
							return;
						case "restocktime":
							itm.restockTime = number;
							db.Query("UPDATE Inventory SET RestockTime = @1 WHERE ID = @0", itm.id, itm.restockTime);
							args.Player.SendSuccessMessage("Changed {0}'s restock time to {1}.", itemName, itm.restockTime);
							if (itm.restockTime < 1 && RestockItems.ContainsItem(itm.id))
							{
								RestockItems.RemoveShopItem(itm.id);
								return;
							}

							if (!RestockItems.ContainsItem(itm.id))
							{
								RestockItems.Add(new ShopItem(itm.id, itm.price, itm.stock, itm.maxStock, itm.restockTime, itm.restockType));
							}
							else
								RestockItems.GetShopItem(itm.id).restockTime = itm.restockTime;
							return;
						case "restocktype":
							if (number != -1 || number != 0 || number != 1)
							{
								args.Player.SendErrorMessage("Restock type number must be one of the following: -1, 0, 1");
								return;
							}
							itm.restockType = number;
							db.Query("UPDATE Inventory SET RestockType = @1 WHERE ID = @0", itm.id, itm.restockType);
							args.Player.SendSuccessMessage("Changed {0}'s restock type to {1}.", itemName, itm.restockType.RestockTypeToString());
							return;
						default:
							args.Player.SendValidModifyUsage();
							return;
					}
				#endregion
				#region reloaddb
				case "reloaddb":
					args.Player.SendInfoMessage("Reloading Inventory and Regions from database....");
					RestockItems.Clear();
					Inventory.Clear();
					Regions.Clear();
					ReadDb();
					args.Player.SendSuccessMessage("Successfully reloaded Inventory and Regions from database!");
					args.Player.SendSuccessMessage("Note, this resets restock timers.");
					return;
				#endregion
				#region region
				case "region":
					if (args.Parameters.Count < 3)
					{
						args.Player.SendValidRegionUsage();
						return;
					}

					var rcmd = args.Parameters[1].ToLower();
					var region = TShock.Regions.GetRegionByName(args.Parameters[2]).Name;

					if (region == null)
					{
						args.Player.SendErrorMessage("Invalid region!");
						return;
					}

					if (rcmd == "add")
					{
						if (Regions.Contains(region))
						{
							args.Player.SendErrorMessage("{0} is already a ServerShop region!", region);
							return;
						}
						Regions.Add(region);
						db.Query("INSERT INTO ShopRegions (Region)" + " VALUES (@0)", region);
						args.Player.SendSuccessMessage("Successfully made {0} a ServerShop region!", region);
					}
					else if (rcmd == "del")
					{
						if (!Regions.Contains(region))
						{
							args.Player.SendErrorMessage("{0} is not a ServerShop region!", region);
							return;
						}
						Regions.Remove(region);
						db.Query("DELETE FROM ShopRegions WHERE Region = @0", region);
						args.Player.SendSuccessMessage("{0} is no longer a ServerShop region!", region);
					}
					else
					{
						args.Player.SendValidRegionUsage();
						return;
					}
					return;
				#endregion
				default:
					args.Player.SendValidAdminShopUsage();
					return;
			}
		}
		#endregion
		#endregion
	}
}
