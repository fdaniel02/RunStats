using RunStats.Garmin;
using RunStats.Processing;

namespace RunStats.Sync;

public class RecordsSync(IGarminGateway garmin, PersonalRecordUpdate personalRecords, RacePredictionUpdate racePredictions)
{
    public async Task<PersonalRecordChanges> UpdatePersonalRecordsAsync(CancellationToken cancellationToken)
    {
        var records = PersonalRecordMapper.Running(await garmin.GetPersonalRecordsAsync(cancellationToken));
        return await personalRecords.ApplyAsync(records, cancellationToken);
    }

    public async Task<int> UpdateRacePredictionsAsync(CancellationToken cancellationToken)
    {
        var predictions = RacePredictionMapper.FromJson(await garmin.GetRacePredictionsAsync(cancellationToken));
        await racePredictions.ApplyAsync(predictions, cancellationToken);
        return predictions.Count;
    }
}
