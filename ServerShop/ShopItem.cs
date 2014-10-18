using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServerShop
{
	public class ShopItem
	{
		public int id;
		public int stock;
		public int price;
		public int maxStock;
		public int restockTime;
		public int restockType;

		public ShopItem(int id = 0, int price = 0, int stock = 0, int maxStock = 0, int restockTime = 0, int restockType = 0)
		{
			this.id = id;
			this.price = price;
			this.stock = stock;
			this.maxStock = maxStock;
			this.restockTime = restockTime;
			this.restockType = restockType;
		}
	}
}
