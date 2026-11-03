export interface AnswerQuestionPageOptions {
    baseUrl: string;
    location: string;

    pageId: string;

    expectedHeading: string;
    answers: FormData;
    expectedRedirect: string;
}