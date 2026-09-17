--Inventory --------------------------------------------
SELECT top(5) * FROM [CapInventory].[cap].[Published] order by Added desc
SELECT top(5) * FROM [CapInventory].[cap].[Received] order by Added desc

SELECT top(5) * FROM [CapInventory].[dbo].[InventoryReservations] order by CreatedAtUtc desc
SELECT top(5) * FROM [CapInventory].[dbo].[Products] order by id
--Update [CapInventory].[dbo].[Products] set AvailableQuantity = 20 where id = 2


--Orders ---------------------------------------------
SELECT top(5) * FROM [CapOrders].[cap].[Published] order by Added desc
SELECT top(5) * FROM [CapOrders].[cap].[Received] order by Added desc

SELECT top(5) * FROM [CapOrders].[dbo].[Orders] order by updatedAtUtc desc
