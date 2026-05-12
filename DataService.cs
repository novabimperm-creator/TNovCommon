using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TNovCommon
{
    public class DataService
    {
        private readonly IDataRepository _repository;

        public DataService(IDataRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        // ---------- Пользовательские данные ----------

        public async Task<string> LoadUserDataAsync(Guid userId, string functionName)
        {
            var entry = await _repository.LoadAsync(userId, functionName);
            return entry.DataJson;
        }

        public async Task SaveUserDataAsync(Guid userId, string functionName, string jsonData)
        {
            ValidateJson(jsonData);
            await _repository.SaveAsync(userId, functionName, jsonData);
        }

        // ---------- Данные модели ----------

        public async Task<string> LoadModelDataAsync(string modelName, string functionName)
        {
            var entry = await _repository.LoadForModelAsync(modelName, functionName);
            return entry.DataJson;
        }

        public async Task SaveModelDataAsync(string modelName, string functionName, string jsonData)
        {
            ValidateJson(jsonData);
            await _repository.SaveForModelAsync(modelName, functionName, jsonData);
        }

        // ---------- Типизированные пользовательские данные ----------
        public async Task<T> LoadUserDataAsync<T>(Guid userId, string functionName)
        {
            string json = await LoadUserDataAsync(userId, functionName);
            return JsonConvert.DeserializeObject<T>(json);
        }

        public async Task SaveUserDataAsync<T>(Guid userId, string functionName, T data)
        {
            string json = JsonConvert.SerializeObject(data);
            await SaveUserDataAsync(userId, functionName, json);
        }

        // ---------- Типизированные данные модели ----------
        public async Task<T> LoadModelDataAsync<T>(string modelName, string functionName)
        {
            string json = await LoadModelDataAsync(modelName, functionName);
            return JsonConvert.DeserializeObject<T>(json);
        }

        public async Task SaveModelDataAsync<T>(string modelName, string functionName, T data)
        {
            string json = JsonConvert.SerializeObject(data);
            await SaveModelDataAsync(modelName, functionName, json);
        }

        // ---------- Вспомогательные методы ----------

        private void ValidateJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("JSON не может быть пустым.");
            JToken.Parse(json); // выбросит исключение при невалидном JSON
        }
    }
}
