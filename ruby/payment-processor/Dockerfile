FROM ruby:3.2-slim

WORKDIR /app
COPY Gemfile ./
RUN bundle install --without development test
COPY app.rb ./

ENV PORT=8083
EXPOSE 8083
CMD ["ruby", "app.rb"]
