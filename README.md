
# Goods Grouper — .NET 8 + Postgres + RabbitMQ + Redis + Mongo + Docker + CI/CD + Nginx

Полный рабочий проект, реализующий тестовое задание: загрузка Excel со списком товаров, их автоматическое разбиение на группы **≤ 200 евро** с суммой максимально близкой к лимиту, периодический запуск каждые 5 минут, отображение списков групп и позиций в группе. 

Стек: **.NET 8, PostgreSQL, RabbitMQ, Redis, MongoDB, Docker, Nginx, GitHub Actions (CI/CD).**

## Как запустить локально (Docker)

```bash
# 1) Клонируем/распаковываем проект
# 2) В корне:
docker compose up -d --build

# API доступен через Nginx на http://localhost/
# Swagger: http://localhost/swagger/index.html
# RabbitMQ UI: http://localhost:15672  (guest/guest)
# Postgres: localhost:5432
# Redis: localhost:6379
# Mongo: localhost:27017
```

## Бизнес-логика (выполнение тестового задания)
- Endpoint для загрузки *.xlsx* — `POST /api/upload` (multipart/form-data). Файл парсится и сохраняется в БД, затем публикуется сообщение в RabbitMQ для запуска группировки.
- Воркер (отдельный контейнер) слушает очередь `grouping.start` и запускает **группировщик**, который жадно наполняет группы, стремясь попасть как можно ближе к 200 евро.
- Также воркер каждые **5 минут** сканирует на наличие необработанных позиций и запускает группировку.
- Получение групп: `GET /api/groups` (кэшируется в Redis).
- Получение позиций по группе: `GET /api/groups/{groupId}/items`.
- Метаданные загрузок сохраняются в MongoDB.

Требования взяты из файла «Тестовое задание. Backend. .docx». Ключевые пункты: загрузка Excel; сервис разбиения на группы ≤200 евро с максимальным приближением; запуск каждые 5 минут; эндпоинты для вывода групп и их товаров; итог — исходники и инструкция запуска. fileciteturn0file0

## Импорт Excel
Ожидается таблица с колонками: **Наименование | Единица измерения | Цена за единицу, евро | Количество, шт.** Начиная со строки 2 (после заголовка).

## Git: где и как сделать коммит
```bash
# Инициализация
git init
git add .
git commit -m "feat: initial commit — goods grouper"

# Создайте публичный/приватный репозиторий на GitHub (например, goods-grouper),
# затем привяжите удалённый origin:
git remote add origin https://github.com/<ВАШ_АККАУНТ>/goods-grouper.git
git branch -M main
git push -u origin main

# Рабочий процесс: feature-ветки + PR
git checkout -b feat/excel-parse
# ... изменения ...
git commit -m "feat(api): excel upload and parse"
git push -u origin feat/excel-parse
# Откройте Pull Request в GitHub и смержите в main
```

## CI/CD (GitHub Actions + GHCR + SSH deploy)
- При пуше в `main` сборка .NET и Docker-образов `api` и `worker`, публикация в **GitHub Container Registry** (ghcr.io).
- Для деплоя добавьте сервер (Ubuntu) с Docker и docker-compose. Настройте секреты:  
  `GHCR_USERNAME`, `GHCR_TOKEN` (PAT со scope `write:packages`), `SSH_HOST`, `SSH_USER`, `SSH_KEY` (private key), `DEPLOY_PATH`.
- Джоб `deploy` (manual `workflow_dispatch`) подключится по SSH и выполнит `docker compose pull && up -d` на сервере.

## Полезные URL
- Swagger: `http://<HOST>/swagger/index.html` (через Nginx)
- RabbitMQ UI: `http://<HOST>:15672` (guest/guest)

## Архитектура
- **Api** — REST + загрузка Excel, публикация задач в RMQ, кэширование ответов.
- **Worker** — обработка очереди и периодический запуск каждые 5 минут.
- **PostgreSQL** — хранилище сущностей (батчи, товары, группы).
- **Redis** — кэш для ускорения списков групп.
- **MongoDB** — метаданные загрузок (сырой след загрузки).
- **RabbitMQ** — транспорт событий между API и воркером.
- **Nginx** — reverse-proxy перед API.

## Примеры запросов
```bash
# Загрузка Excel
curl -F "file=@/path/to/input.xlsx" http://localhost/api/upload

# Получение групп
curl http://localhost/api/groups

# Позиции по группе
curl http://localhost/api/groups/<groupId>/items
```

## Примечания по алгоритму
Алгоритм реализован как **жадный "best fit"**: на каждом шаге выбирается самая дорогая позиция, которая ещё помещается под текущий лимит 200€. Это быстро и хорошо приближает сумму к лимиту. При необходимости легко заменить на более сложную эвристику/поиск.

## Локальная разработка без Docker
```bash
# Требуется .NET 8 и локальные сервисы (Postgres, RabbitMQ, Redis, Mongo)
cd src/Api
dotnet run

cd ../Worker
dotnet run
```

---

### MBTI и самооценка (из тестового файла)
- **Самооценка**: добавьте таблицу с критериями и средним значением в корень репозитория либо в Wiki (см. файл задания). fileciteturn0file0
- **MBTI**: загрузите скриншот результата теста в папку `docs/` репозитория. fileciteturn0file0
