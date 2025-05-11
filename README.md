Итак

Доступ по:
http://localhost:5252/api/data

ВПН не забудьте включать окда?

Эндпоинт пока один(можно добавлять) - api/data

Параметры:
    - GET: curl http://localhost:5252/api/data?prompt=smth&iname=smth

    - POST: {
        "prompt": "Напиши код на C#, который парсит JSON с экранированными \"кавычками\"",
        "iname" : "Новая нейронка из списка в GeminiApi.cs"
        }

Рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси рокси 


Для перевода: dotnet add package Newtonsoft.Json
Для бд: dotnet add package Npgsql


Какие методы есть у сервака и примеры обращения:

   Запрос для получения рецептов (он просто выведет когда докупка true):
    curl -X POST \
      -H "Content-Type: application/json" \
      -d '{"ingredients":["сахар"], "count" :\
       10 }' \

      http://localhost:5252/searchRecipe/data


   Где count: кол-во рецептов которое тербуется
       ingredients: массив продуктов на русском языке 

   Запрос для получения рецептов:
    curl -X POST \
      -H "Content-Type: application/json" \
      -d '{"ingredients":["творожный сыр", "соль"], "count" :\
       1, "purchase" : true}' \

    http://localhost:5252/getRecipesBuyOrNo/data
   Тут purchase - это флаг докупка/нет

   Запрос для получения рецепта по его id:
     curl -X POST \
      -H "Content-Type: application/json" \
      -d '{"id" : 23 }' \

    http://localhost:5252/searchRecipebyid/data
   id - PrimaryKey в таблице


