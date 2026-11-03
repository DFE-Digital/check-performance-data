// export interface Pupil {
//     SelectedPupilId: string;
//     SelectedPupilLabel: string;
//}

export interface Pupil {
    id: string;
    cypmdId: string;
    firstName: string;
    surname: string;
    dateOfBirth: string;

    /*
     * Inclusion status. KS4: the P_INCL code from the
     * data file. 16 to 19: INCLUDED or NOT INCLUDED,
     * from the INCLUDED flag.
     */
    inclusionCode: string;

    /*
     * Label the application stores for the selected pupil,
     * in the format of its pupil-search suggestion.
     */
    searchLabel: string;

    /*
     * Result key (QAN|SESSION|SOURCE) for a pupil with a
     * result in the results file.
     */
    resultKey?: string;
}
