using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TShockAPI.DB;

using TShockAPI;

namespace ServerShop
{
	public static class Extensions
	{
		public static ShopItem GetShopItem(this List<ShopItem> lsItems, string name)
		{
			foreach (ShopItem item in lsItems)
			{
				if (TShock.Utils.GetItemById(item.id).name == name)
				{
					return item;
				}
			}
			return null;
		}

		public static ShopItem GetShopItem(this List<ShopItem> lsItems, int id)
		{
			foreach (ShopItem item in lsItems)
			{
				if (item.id == id)
				{
					return item;
				}
			}
			return null;
		}

		public static bool IsInShopRegion(this TSPlayer plr, List<string> regions)
		{
			Region r;
			foreach (var reg in regions)
			{
				r = TShock.Regions.GetRegionByName(reg);
				if (r != null && r.InArea((int)(plr.X / 16), (int)(plr.Y / 16)))
					return true;
			}
			plr.SendErrorMessage("You are not in a valid ServerShop region!");
			return false;
		}

		public static void RemoveShopItem(this List<ShopItem> lsItems, int id)
		{
			ShopItem item = lsItems.GetShopItem(id);
			lsItems.Remove(item);
		}

		public static bool ContainsItem(this List<ShopItem> lsItems, int id)
		{
			foreach (ShopItem item in lsItems)
			{
				if (item.id == id)
				{
					return true;
				}
			}
			return false;
		}

		public static string RestockTypeToString(this int type)
		{
			if (type == 1)
				return "surplus";
			else if (type == 0)
				return "default";
			else
				return "shortage";
		}

		public static void SendValidShopUsage(this TSPlayer player)
		{
			player.SendInfoMessage("Proper shop usage:");
			player.SendInfoMessage("/ss buy <item name> [amount]");
			player.SendInfoMessage("/ss sell <item name> [amount]");
			player.SendInfoMessage("/ss search <item name or id>");
		}

		public static void SendValidAdminShopUsage(this TSPlayer player)
		{
			player.SendInfoMessage("Proper admin shop usage: /ssa <parameters>");
			player.SendInfoMessage("List of admin shop parameters");
			player.SendInfoMessage("populatedb, balance, modify, reloaddb, region");
		}

		public static void SendValidBalanceUsage(this TSPlayer player)
		{
			player.SendInfoMessage("Proper balance usage:");
			player.SendInfoMessage("/ssa balance <subcmd>");
			player.SendInfoMessage("List of balance subcommands:");
			player.SendInfoMessage("-shortage, -default, -surplus");
		}

		public static void SendValidModifyUsage(this TSPlayer player)
		{
			player.SendInfoMessage("Proper modify usage:");
			player.SendInfoMessage("/ssa modify <item id or name> <subcmd> <number>");
			player.SendInfoMessage("List of modify subcommands:");
			player.SendInfoMessage("price, stock, maxstock, restocktime");
		}

		public static void SendValidRegionUsage(this TSPlayer player)
		{
			player.SendInfoMessage("Proper region usage:");
			player.SendInfoMessage("/ssa region <add/del> <region name>");
		}
	}
}
