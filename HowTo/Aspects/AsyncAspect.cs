﻿using System;
using System.Threading;

using NUnit.Framework;

using BLToolkit.Aspects;
using BLToolkit.Reflection;

namespace HowTo.Aspects
{
	public /*[a]*/abstract/*[/a]*/ class AsyncTestObject
	{
		public /*[a]*/int/*[/a]*/ /*[a]*/Test/*[/a]*/(/*[a]*/int intVal/*[/a]*/, /*[a]*/string strVal/*[/a]*/)
		{
			Thread.Sleep(200);
			return intVal;
		}

		[/*[a]*/Async/*[/a]*/] public abstract /*[a]*/IAsyncResult/*[/a]*/ /*[a]*/BeginTest/*[/a]*/(/*[a]*/int intVal/*[/a]*/, /*[a]*/string strVal/*[/a]*/);
		[/*[a]*/Async/*[/a]*/] public abstract /*[a]*/IAsyncResult/*[/a]*/ /*[a]*/BeginTest/*[/a]*/(/*[a]*/int intVal/*[/a]*/, /*[a]*/string strVal/*[/a]*/, /*[a]*/AsyncCallback/*[/a]*/ callback);
		[/*[a]*/Async/*[/a]*/] public abstract /*[a]*/IAsyncResult/*[/a]*/ /*[a]*/BeginTest/*[/a]*/(/*[a]*/int intVal/*[/a]*/, /*[a]*/string strVal/*[/a]*/, /*[a]*/AsyncCallback/*[/a]*/ callback, /*[a]*/object/*[/a]*/ state);
		[/*[a]*/Async/*[/a]*/] public abstract /*[a]*/int/*[/a]*/          /*[a]*/EndTest/*[/a]*/  (/*[a]*/IAsyncResult/*[/a]*/ asyncResult);

		[/*[a]*/Async/*[/a]*/("Test")]
		public abstract /*[a]*/IAsyncResult/*[/a]*/ AnyName(/*[a]*/int intVal/*[/a]*/, /*[a]*/string strVal/*[/a]*/, /*[a]*/AsyncCallback/*[/a]*/ callback, /*[a]*/object/*[/a]*/ state);

		[/*[a]*/Async/*[/a]*/("Test", typeof(int), typeof(string))]
		public abstract /*[a]*/int/*[/a]*/          AnyName(/*[a]*/IAsyncResult/*[/a]*/ asyncResult);
	}

	[TestFixture]
	public class AsyncAspect
	{
		[Test]
		public void AsyncTest()
		{
			AsyncTestObject o = TypeAccessor<AsyncTestObject>.CreateInstance();

			IAsyncResult ar = o.BeginTest(1, "10");
			Assert.AreEqual(1, o.EndTest(ar));
		}

		private static void CallBack(IAsyncResult ar)
		{
			Console.WriteLine("Callback");

			AsyncTestObject o = (AsyncTestObject)ar.AsyncState;
			o.EndTest(ar);
		}

		[Test]
		public void CallbackTest()
		{
			AsyncTestObject o = TypeAccessor<AsyncTestObject>.CreateInstance();

			o.BeginTest(2, null, new AsyncCallback(CallBack), o);
		}

		[Test]
		public void AnyNameTest()
		{
			AsyncTestObject o = TypeAccessor<AsyncTestObject>.CreateInstance();

			IAsyncResult ar = o.AnyName(2, null, null, null);
			Assert.AreEqual(2, o.AnyName(ar));
		}
	}
}