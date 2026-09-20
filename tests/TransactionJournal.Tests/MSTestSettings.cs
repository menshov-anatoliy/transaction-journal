// Тесты журнала выполняются строго последовательно: проверки используют
// процесс-глобальное состояние — переменные окружения и пул соединений SQLite.
using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: DoNotParallelize]

