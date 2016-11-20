﻿using System;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;

namespace Website.Classes
{
	public class Movie
	{
		public string url { get; set; }
		public int id { get; set; }
	}

	public class Product
	{
		public string url { get; set; }
		public int id { get; set; }
	}

	public class JsonSitemap
	{
		public IList<Movie> movies { get; set; }
		public IList<Product> products { get; set; }
	}
}